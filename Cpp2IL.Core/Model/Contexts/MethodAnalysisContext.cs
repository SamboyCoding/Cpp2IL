using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Analysis;
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

    public ulong Rva => UnderlyingPointer == 0 ? 0 : AppContext.Binary.GetRva(UnderlyingPointer);

    /// <summary>
    /// The raw method body as machine code in the active instruction set.
    /// </summary>
    public BinarySlice RawBytes = BinarySlice.Empty;

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

    private const int MaxMethodSizeBytes = 18000; // 18KB

    public List<ParameterAnalysisContext> Parameters = [];

    public List<LocalVariable> ParameterLocals = [];

    /// <summary>
    /// Does this method return void?
    /// </summary>
    public bool IsVoid => ReturnType == AppContext.SystemTypes.SystemVoidType;

    public bool IsStatic => (Attributes & MethodAttributes.Static) != 0;

    public bool IsVirtual => (Attributes & MethodAttributes.Virtual) != 0;

    public bool IsAbstract => (Attributes & MethodAttributes.Abstract) != 0;

    public bool IsNewSlot => (Attributes & MethodAttributes.NewSlot) != 0;

    public bool IsFinal => (Attributes & MethodAttributes.Final) != 0;

    protected override int CustomAttributeIndex => Definition?.customAttributeIndex ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeIndex if they have custom attributes");

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType?.DeclaringAssembly ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeAssembly if they have custom attributes");

    public override string DefaultName => Definition?.Name ?? throw new("Subclasses of MethodAnalysisContext should override DefaultName");

    public string FullName => DeclaringType == null ? Name : $"{DeclaringType.FullName}::{Name}";

    public string FullNameWithSignature => $"{ReturnType.FullName} {FullName}({string.Join(", ", Parameters.Select(p => p.HumanReadableSignature))})";

    public virtual MethodAttributes DefaultAttributes => Definition?.Attributes ?? throw new($"Subclasses of MethodAnalysisContext should override {nameof(DefaultAttributes)}");

    public virtual MethodAttributes? OverrideAttributes { get; set; }

    public MethodAttributes Attributes
    {
        get => OverrideAttributes ?? DefaultAttributes;
        set => OverrideAttributes = value;
    }

    public virtual MethodImplAttributes DefaultImplAttributes => Definition?.MethodImplAttributes ?? throw new($"Subclasses of MethodAnalysisContext should override {nameof(DefaultImplAttributes)}");

    public virtual MethodImplAttributes? OverrideImplAttributes { get; set; }

    public MethodImplAttributes ImplAttributes
    {
        get => OverrideImplAttributes ?? DefaultImplAttributes;
        set => OverrideImplAttributes = value;
    }

    public MethodAttributes Visibility
    {
        get
        {
            return Attributes & MethodAttributes.MemberAccessMask;
        }
        set
        {
            Attributes = (Attributes & ~MethodAttributes.MemberAccessMask) | (value & MethodAttributes.MemberAccessMask);
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

    public virtual TypeAnalysisContext DefaultReturnType => AppContext.ResolveIl2CppType(Definition?.RawReturnType) ?? throw new($"Subclasses of MethodAnalysisContext should override {nameof(DefaultReturnType)}");

    public TypeAnalysisContext? OverrideReturnType { get; set; }

    //TODO Support custom attributes on return types (v31 feature)
    public TypeAnalysisContext ReturnType
    {
        get => OverrideReturnType ?? DefaultReturnType;
        set => OverrideReturnType = value;
    }

    public MethodAnalysisContext? BaseMethod
    {
        get
        {
            if (Definition == null)
                return null;

            var vtable = DeclaringType?.Definition?.VTable;
            if (vtable == null)
                return null;

            for (var i = 0; i < vtable.Length; ++i)
            {
                var vtableEntry = vtable[i];
                if (vtableEntry is null or { Type: not MetadataUsageType.MethodDef } || vtableEntry.AsMethod() != Definition)
                    continue;

                if (IsInterfaceSlot(this, i))
                {
                    continue;
                }

                var baseType = DeclaringType?.DefaultBaseType;
                while (baseType is not null)
                {
                    if (TryGetMethodForSlot(baseType, i, out var method))
                    {
                        return method;
                    }
                    baseType = baseType.DefaultBaseType;
                }
            }
            return null;
        }
    }

    private List<MethodAnalysisContext>? _overrides;

    /// <summary>
    /// The set of interface methods which this method explicitly overrides.
    /// </summary>
    public List<MethodAnalysisContext> Overrides
    {
        get
        {
            // Lazy load the overrides
            return _overrides ??= GetOverrides().ToList();
        }
    }

    private IEnumerable<MethodAnalysisContext> GetOverrides()
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

        IEnumerable<MethodAnalysisContext> GetOverriddenMethods(Il2CppTypeDefinition declaringTypeDefinition, MetadataUsage?[] vtable)
        {
            for (var i = 0; i < vtable.Length; ++i)
            {
                var vtableEntry = vtable[i];
                if (vtableEntry is null or { Type: not MetadataUsageType.MethodDef })
                    continue;

                if (vtableEntry.AsMethod() != Definition)
                    continue;

                // Interface inheritance
                foreach (var interfaceOffset in declaringTypeDefinition.InterfaceOffsets)
                {
                    if (i >= interfaceOffset.offset)
                    {
                        var interfaceTypeContext = interfaceOffset.Type.ToContext(AppContext);
                        var slot = i - interfaceOffset.offset;
                        if (interfaceTypeContext != null && TryGetMethodForSlot(interfaceTypeContext, slot, out var method) && !IsInterfaceSlot(method, slot))
                        {
                            yield return method;
                        }
                    }
                }
            }
        }
    }

    private static bool IsInterfaceSlot(MethodAnalysisContext method, int slot)
    {
        var declaringTypeDefinition = method.DeclaringType?.Definition;
        if (declaringTypeDefinition == null)
            return false;

        foreach (var interfaceOffset in declaringTypeDefinition.InterfaceOffsets)
        {
            if (slot >= interfaceOffset.offset)
            {
                var interfaceTypeContext = interfaceOffset.Type.ToContext(method.AppContext);
                if (interfaceTypeContext != null && HasMethodForSlot(interfaceTypeContext, slot - interfaceOffset.offset))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool HasMethodForSlot(TypeAnalysisContext declaringType, int slot)
    {
        if (declaringType is GenericInstanceTypeAnalysisContext genericInstanceType)
        {
            return genericInstanceType.GenericType.Methods.Any(m => m.Slot == slot);
        }
        else
        {
            return declaringType.Methods.Any(m => m.Slot == slot);
        }
    }

    private static bool TryGetMethodForSlot(TypeAnalysisContext declaringType, int slot, [NotNullWhen(true)] out MethodAnalysisContext? method)
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
    }

    public void EnsureRawBytes()
    {
        //Some abstract methods (on interfaces, no less) apparently have a body? Unity doesn't support default interface methods so idk what's going on here.
        //E.g. UnityEngine.Purchasing.AppleCore.dll: UnityEngine.Purchasing.INativeAppleStore::SetUnityPurchasingCallback on among us (itch.io build)
        if (Definition != null && Definition.MethodPointer != 0 && !Definition.Attributes.HasFlag(MethodAttributes.Abstract))
        {
            RawBytes = AppContext.InstructionSet.GetRawBytesForMethod(this, this is AttributeGeneratorMethodAnalysisContext);

            if (RawBytes.Length == 0)
            {
                Logger.VerboseNewline("\t\t\tUnexpectedly got 0-byte method body for " + this + $". Pointer was 0x{Definition.MethodPointer:X}", "MAC");
            }
        }
    }

    protected MethodAnalysisContext(ApplicationAnalysisContext context) : base(0, context)
    { }

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

        // Indirect jumps/calls should probably be resolved here before stack analysis

        StackAnalyzer.Analyze(this);

        // Dominator info must be computed after stack analysis, which removes unreachable/empty
        // blocks and would otherwise leave the dominator tree out of sync with the graph SSA sees.
        DominatorInfo = new DominatorInfo(ControlFlowGraph);

        // Create locals
        SsaForm.Build(this);
        LocalVariables.CreateAll(this);

        // Fold the explicit per-comparison flag arithmetic back into single relational comparisons,
        // then eliminate the now-dead flag computations. Both run in SSA form, where each
        // flag/temporary has a single, version-stable definition.
        FlagConditionRecovery.Run(this);
        DeadCodeEliminator.Run(this);

        // Resolve call targets, strings and getters, then run the combined type-propagation and
        // field-resolution fixpoint - all while still in SSA form, so every local is
        // single-assignment and a type, once known, is stable for that value.
        MetadataResolver.ResolveAll(this);

        // Resolve KeyFunctionAddress calls.
        KeyFunctionRecovery.Run(this);

        // Delete any il2cpp_codegen_initialize_runtime_metadata/il2cpp_codegen_initialize_method
        MetadataInitGuardRemover.Run(this);

        LocalVariables.ResolveTypesAndFields(this);

        // Copy/constant propagation belongs in SSA, where one definition dominates all uses and phis
        // make joins explicit, so forwarding a value is an unconditional global substitution.
        SsaSimplifier.Run(this);

        SsaForm.Remove(this);

        // Now out of SSA: clean up the per-edge copies that phi removal introduced (a local can have
        // several definitions merging at a join here, so this pass propagates conservatively), then
        // drop dead locals.
        Simplifier.Simplify(this);

        // Fix float literals
        FloatLiteralRecovery.Run(this);

        LocalVariables.RemoveUnused(this);
    }

    public void AddWarning(string warning) => AnalysisWarnings.Add(warning);

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

    public ConcreteGenericMethodAnalysisContext MakeConcreteGenericMethod(IEnumerable<TypeAnalysisContext> typeGenericParameters, IEnumerable<TypeAnalysisContext> methodGenericParameters)
    {
        if (this is ConcreteGenericMethodAnalysisContext)
        {
            throw new InvalidOperationException($"Attempted to make a {nameof(ConcreteGenericMethodAnalysisContext)} concrete: {this}");
        }
        else
        {
            return new ConcreteGenericMethodAnalysisContext(this, typeGenericParameters, methodGenericParameters);
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
