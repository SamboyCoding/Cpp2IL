using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Actions;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using StableNameDotNet.Providers;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents one method within the application. Can be analyzed to attempt to reconstruct the function body.
/// </summary>
public class MethodAnalysisContext : HasGenericParameters, IMethodInfoProvider
{
    /// <summary>
    /// The underlying metadata for the method.
    ///
    /// Nullable iff this is a subclass.
    /// </summary>
    public readonly Il2CppMethodDefinition? Definition;

    /// <summary>
    /// The analysis context for the declaring type of this method.
    /// </summary>
    public readonly TypeAnalysisContext? DeclaringType;

    /// <summary>
    /// The address of this method as defined in the underlying metadata.
    /// </summary>
    public virtual ulong UnderlyingPointer => Definition?.MethodPointer ?? throw new("Subclasses of MethodAnalysisContext should override UnderlyingPointer");

    public ulong Rva => UnderlyingPointer == 0 || LibCpp2IlMain.Binary == null ? 0 : LibCpp2IlMain.Binary.GetRva(UnderlyingPointer);

    /// <summary>
    /// The raw method body as machine code in the active instruction set.
    /// </summary>
    public Memory<byte> RawBytes => rawMethodBody ??= InitRawBytes();

    /// <summary>
    /// The first-stage-analyzed Instruction-Set-Independent Language Instructions.
    /// </summary>
    public List<Instruction>? ConvertedIsil;

    /// <summary>
    /// All ISIL local variables.
    /// </summary>
    public List<LocalVariable> Locals = [];

    /// <summary>
    /// Operands used as parameters.
    /// </summary>
    public List<object> ParameterOperands = [];

    /// <summary>
    /// The control flow graph for this method, if one is built.
    /// </summary>
    public ISILControlFlowGraph? ControlFlowGraph;

    /// <summary>
    /// Dominance info for the control flow graph.
    /// </summary>
    public DominatorInfo? DominatorInfo;

    public List<string> AnalysisWarnings = [];

    private const int MaxMethodSizeBytes = 256000; // 256KB

    public List<ParameterAnalysisContext> Parameters = [];

    public List<LocalVariable> ParameterLocals = [];

    /// <summary>
    /// Does this method return void?
    /// </summary>
    public bool IsVoid => ReturnType == AppContext.SystemTypes.SystemVoidType;

    public bool IsStatic => (Attributes & MethodAttributes.Static) != 0;

    protected override int CustomAttributeIndex => Definition?.customAttributeIndex ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeIndex if they have custom attributes");

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType?.DeclaringAssembly ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeAssembly if they have custom attributes");

    public override string DefaultName => Definition?.Name ?? throw new("Subclasses of MethodAnalysisContext should override DefaultName");

    public string FullName => DeclaringType == null ? Name : $"{DeclaringType.FullName}::{Name}";

    public string FullNameWithSignature => $"{ReturnType.FullName} {FullName}({string.Join(", ", Parameters.Select(p => p.HumanReadableSignature))})";

    public virtual MethodAttributes DefaultAttributes => Definition?.Attributes ?? throw new($"Subclasses of MethodAnalysisContext should override {nameof(DefaultAttributes)}");

    public virtual MethodAttributes? OverrideAttributes { get; set; }

    public MethodAttributes Attributes => OverrideAttributes ?? DefaultAttributes;

    public virtual MethodImplAttributes DefaultImplAttributes => Definition?.MethodImplAttributes ?? throw new($"Subclasses of MethodAnalysisContext should override {nameof(DefaultImplAttributes)}");

    public virtual MethodImplAttributes? OverrideImplAttributes { get; set; }

    public MethodImplAttributes ImplAttributes => OverrideImplAttributes ?? DefaultImplAttributes;

    public MethodAttributes Visibility
    {
        get
        {
            return Attributes & MethodAttributes.MemberAccessMask;
        }
        set
        {
            OverrideAttributes = (Attributes & ~MethodAttributes.MemberAccessMask) | (value & MethodAttributes.MemberAccessMask);
        }
    }

    private List<GenericParameterTypeAnalysisContext>? _genericParameters;
    public override List<GenericParameterTypeAnalysisContext> GenericParameters
    {
        get
        {
            // Lazy load the generic parameters
            _genericParameters ??= Definition?.GenericContainer?.GenericParameters.Select(p => new GenericParameterTypeAnalysisContext(p, this)).ToList() ?? [];
            return _genericParameters;
        }
    }

    private ushort Slot => Definition?.slot ?? ushort.MaxValue;

    public virtual TypeAnalysisContext DefaultReturnType => DeclaringType?.DeclaringAssembly.ResolveIl2CppType(Definition?.RawReturnType) ?? throw new($"Subclasses of MethodAnalysisContext should override {nameof(DefaultReturnType)}");

    public TypeAnalysisContext? OverrideReturnType { get; set; }

    //TODO Support custom attributes on return types (v31 feature)
    public TypeAnalysisContext ReturnType => OverrideReturnType ?? DefaultReturnType;

    protected Memory<byte>? rawMethodBody;

    public MethodAnalysisContext? BaseMethod => Overrides.FirstOrDefault(m => m.DeclaringType?.IsInterface is false);

    /// <summary>
    /// The set of methods which this method overrides.
    /// </summary>
    public virtual IEnumerable<MethodAnalysisContext> Overrides
    {
        get
        {
            if (Definition == null)
                return [];

            var declaringTypeDefinition = DeclaringType?.Definition;
            if (declaringTypeDefinition == null)
                return [];

            var vtable = declaringTypeDefinition.VTable;
            if (vtable == null)
                return [];

            return GetOverriddenMethods(declaringTypeDefinition, vtable);

            bool TryGetMethodForSlot(TypeAnalysisContext declaringType, int slot, [NotNullWhen(true)] out MethodAnalysisContext? method)
            {
                if (declaringType is GenericInstanceTypeAnalysisContext genericInstanceType)
                {
                    var genericMethod = genericInstanceType.GenericType.Methods.FirstOrDefault(m => m.Slot == slot);
                    if (genericMethod is not null)
                    {
                        method = new ConcreteGenericMethodAnalysisContext(genericMethod, genericInstanceType.GenericArguments, []);
                        return true;
                    }
                }
                else
                {
                    var baseMethod = declaringType.Methods.FirstOrDefault(m => m.Slot == slot);
                    if (baseMethod is not null)
                    {
                        method = baseMethod;
                        return true;
                    }
                }

                method = null;
                return false;
            }

            IEnumerable<MethodAnalysisContext> GetOverriddenMethods(Il2CppTypeDefinition declaringTypeDefinition, MetadataUsage?[] vtable)
            {
                for (var i = 0; i < vtable.Length; ++i)
                {
                    var vtableEntry = vtable[i];
                    if (vtableEntry is null or { Type: not MetadataUsageType.MethodDef })
                        continue;

                    if (vtableEntry.AsMethod() != Definition)
                        continue;

                    // Normal inheritance
                    var baseType = DeclaringType?.BaseType;
                    while (baseType is not null)
                    {
                        if (TryGetMethodForSlot(baseType, i, out var method))
                        {
                            yield return method;
                            break; // We only want direct overrides, not the entire inheritance chain.
                        }
                        baseType = baseType.BaseType;
                    }

                    // Interface inheritance
                    foreach (var interfaceOffset in declaringTypeDefinition.InterfaceOffsets)
                    {
                        if (i >= interfaceOffset.offset)
                        {
                            var interfaceTypeContext = interfaceOffset.Type.ToContext(CustomAttributeAssembly);
                            if (interfaceTypeContext != null && TryGetMethodForSlot(interfaceTypeContext, i - interfaceOffset.offset, out var method))
                            {
                                yield return method;
                            }
                        }
                    }
                }
            }
        }
    }

    private static readonly List<IAction> analysisActions =
    [
        // Indirect jumps/calls should probably be resolved here before stack analysis
        new StackAnalyzer() { MaxBlockVisitCount = 5000 },
        new BuildSsaForm(),
        new CreateLocals(),
        new ResolveCalls(),
        new ApplyMetadata(),
        new RemoveSsaForm(),
        new Inlining(),
        new PropagateTypes() { MaxLoopCount = 5000 },
        new ResolveFieldOffsets(),
        new RemoveUnusedLocals(),
        new ResolveGetters()
    ];

    public MethodAnalysisContext(Il2CppMethodDefinition? definition, TypeAnalysisContext parent) : base(definition?.token ?? 0, parent.AppContext)
    {
        DeclaringType = parent;
        Definition = definition;

        if (Definition != null)
        {
            InitCustomAttributeData();

            for (var i = 0; i < Definition.InternalParameterData!.Length; i++)
            {
                var parameterDefinition = Definition.InternalParameterData![i];
                Parameters.Add(new(parameterDefinition, i, this));
            }
        }
        else
            rawMethodBody = Array.Empty<byte>();
    }

    [MemberNotNull(nameof(rawMethodBody))]
    public void EnsureRawBytes()
    {
        rawMethodBody ??= InitRawBytes();
    }

    private Memory<byte> InitRawBytes()
    {
        //Some abstract methods (on interfaces, no less) apparently have a body? Unity doesn't support default interface methods so idk what's going on here.
        //E.g. UnityEngine.Purchasing.AppleCore.dll: UnityEngine.Purchasing.INativeAppleStore::SetUnityPurchasingCallback on among us (itch.io build)
        if (Definition != null && Definition.MethodPointer != 0 && !Definition.Attributes.HasFlag(MethodAttributes.Abstract))
        {
            var ret = AppContext.InstructionSet.GetRawBytesForMethod(this, false);

            if (ret.Length == 0)
            {
                Logger.VerboseNewline("\t\t\tUnexpectedly got 0-byte method body for " + this + $". Pointer was 0x{Definition.MethodPointer:X}", "MAC");
            }

            return ret;
        }
        else
            return Array.Empty<byte>();
    }

    protected MethodAnalysisContext(ApplicationAnalysisContext context) : base(0, context)
    {
        rawMethodBody = Array.Empty<byte>();
    }

    [MemberNotNull(nameof(ConvertedIsil))]
    public void Analyze()
    {
        if (RawBytes.Length > MaxMethodSizeBytes)
        {
            Logger.WarnNewline($"Method {FullName} is too big ({RawBytes.Length} bytes), skipping analysis.");
            ConvertedIsil = [];
            return;
        }

        if (ConvertedIsil != null)
            return;

        if (UnderlyingPointer == 0)
        {
            ConvertedIsil = [];
            return;
        }

        ConvertedIsil = AppContext.InstructionSet.GetIsilFromMethod(this);
        ParameterOperands = AppContext.InstructionSet.GetParameterOperandsFromMethod(this);

        if (ConvertedIsil.Count == 0)
            return; //Nothing to do, empty function

        ControlFlowGraph = new ISILControlFlowGraph(ConvertedIsil);
        DominatorInfo = new DominatorInfo(ControlFlowGraph);

        foreach (var action in analysisActions)
            action.Apply(this);
    }

    public void AddWarning(string warning) => AnalysisWarnings.Add($"warning: {warning}");
    public void AddError(string error) => AnalysisWarnings.Add($"error: {error}");

    public void ReleaseAnalysisData()
    {
        ConvertedIsil = null;
        ControlFlowGraph = null;
        DominatorInfo = null;
    }

    public ConcreteGenericMethodAnalysisContext MakeGenericInstanceMethod(params IEnumerable<TypeAnalysisContext> methodGenericParameters)
    {
        if (this is ConcreteGenericMethodAnalysisContext methodOnGenericInstanceType)
        {
            return new ConcreteGenericMethodAnalysisContext(methodOnGenericInstanceType.BaseMethodContext, methodOnGenericInstanceType.TypeGenericParameters, methodGenericParameters);
        }
        else
        {
            return new ConcreteGenericMethodAnalysisContext(this, [], methodGenericParameters);
        }
    }

    public override string ToString() => $"Method: {FullName}";

    #region StableNameDot implementation

    ITypeInfoProvider IMethodInfoProvider.ReturnType =>
        Definition!.RawReturnType!.ThisOrElementIsGenericParam()
            ? new GenericParameterTypeInfoProviderWrapper(Definition.RawReturnType!.GetGenericParamName())
            : TypeAnalysisContext.GetSndnProviderForType(AppContext, Definition!.RawReturnType);

    IEnumerable<IParameterInfoProvider> IMethodInfoProvider.ParameterInfoProviders => Parameters;

    string IMethodInfoProvider.MethodName => Name;

    MethodAttributes IMethodInfoProvider.MethodAttributes => Attributes;

    MethodSemantics IMethodInfoProvider.MethodSemantics
    {
        get
        {
            if (DeclaringType != null)
            {
                //This one is a bit trickier, as il2cpp doesn't use semantics.
                foreach (var prop in DeclaringType.Properties)
                {
                    if (prop.Getter == this)
                        return MethodSemantics.Getter;
                    if (prop.Setter == this)
                        return MethodSemantics.Setter;
                }

                foreach (var evt in DeclaringType.Events)
                {
                    if (evt.Adder == this)
                        return MethodSemantics.AddOn;
                    if (evt.Remover == this)
                        return MethodSemantics.RemoveOn;
                    if (evt.Invoker == this)
                        return MethodSemantics.Fire;
                }
            }

            return 0;
        }
    }

    #endregion
}
