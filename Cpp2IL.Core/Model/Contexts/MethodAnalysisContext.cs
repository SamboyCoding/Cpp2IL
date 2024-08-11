using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Graphs.Intermediate;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using StableNameDotNet.Providers;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents one method within the application. Can be analyzed to attempt to reconstruct the function body.
/// </summary>
public class MethodAnalysisContext : HasCustomAttributesAndName, IMethodInfoProvider
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
    public Memory<byte> RawBytes;

    /// <summary>
    /// The first-stage-analyzed Instruction-Set-Independent Language Instructions.
    /// </summary>
    public List<InstructionSetIndependentInstruction>? ConvertedIsil;

    /// <summary>
    /// The control flow graph for this method, if one is built.
    /// </summary>
    public ISILControlFlowGraph? ControlFlowGraph;

    public List<ParameterAnalysisContext> Parameters = new();

    /// <summary>
    /// Does this method return void?
    /// </summary>
    public virtual bool IsVoid => (Definition?.ReturnType?.ToString() ?? throw new("Subclasses of MethodAnalysisContext should override IsVoid")) == "System.Void";

    public virtual bool IsStatic => Definition?.IsStatic ?? throw new("Subclasses of MethodAnalysisContext should override IsStatic");

    protected override int CustomAttributeIndex => Definition?.customAttributeIndex ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeIndex if they have custom attributes");

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType?.DeclaringAssembly ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeAssembly if they have custom attributes");

    public override string DefaultName => Definition?.Name ?? throw new("Subclasses of MethodAnalysisContext should override DefaultName");

    public virtual MethodAttributes Attributes => Definition?.Attributes ?? throw new("Subclasses of MethodAnalysisContext should override Attributes");

    public TypeAnalysisContext? InjectedReturnType { get; set; }

    public int ParameterCount => Parameters.Count;

    //TODO Support custom attributes on return types (v31 feature)
    public TypeAnalysisContext ReturnTypeContext => InjectedReturnType ?? DeclaringType!.DeclaringAssembly.ResolveIl2CppType(Definition!.RawReturnType!);


    private static List<IBlockProcessor> blockProcessors = new List<IBlockProcessor>()
    {
        new StringProcessor()
    };

    public MethodAnalysisContext(Il2CppMethodDefinition? definition, TypeAnalysisContext parent) : base(definition?.token ?? 0, parent.AppContext)
    {
        DeclaringType = parent;
        Definition = definition;

        if (Definition != null)
        {
            InitCustomAttributeData();

            //Some abstract methods (on interfaces, no less) apparently have a body? Unity doesn't support default interface methods so idk what's going on here.
            //E.g. UnityEngine.Purchasing.AppleCore.dll: UnityEngine.Purchasing.INativeAppleStore::SetUnityPurchasingCallback on among us (itch.io build)
            if (Definition.MethodPointer != 0 && !Definition.Attributes.HasFlag(MethodAttributes.Abstract))
            {
                RawBytes = AppContext.InstructionSet.GetRawBytesForMethod(this, false);

                if (RawBytes.Length == 0)
                {
                    Logger.VerboseNewline("\t\t\tUnexpectedly got 0-byte method body for " + this + $". Pointer was 0x{Definition.MethodPointer:X}", "MAC");
                }
            }
            else
                RawBytes = Array.Empty<byte>();

            for (var i = 0; i < Definition.InternalParameterData!.Length; i++)
            {
                var parameterDefinition = Definition.InternalParameterData![i];
                Parameters.Add(new(parameterDefinition, i, this));
            }
        }
        else
            RawBytes = Array.Empty<byte>();
    }

    protected MethodAnalysisContext(ApplicationAnalysisContext context) : base(0, context)
    {
        RawBytes = Array.Empty<byte>();
    }

    [MemberNotNull(nameof(ConvertedIsil))]
    public void Analyze()
    {
        if (ConvertedIsil != null)
            return;

        if (UnderlyingPointer == 0)
        {
            ConvertedIsil = new(0);
            return;
        }

        ConvertedIsil = AppContext.InstructionSet.GetIsilFromMethod(this);

        if (ConvertedIsil.Count == 0)
            return; //Nothing to do, empty function

        ControlFlowGraph = new ISILControlFlowGraph();
        ControlFlowGraph.Build(ConvertedIsil);

        // Post step to convert metadata usage. Ldstr Opcodes etc.
        foreach (var block in ControlFlowGraph.Blocks)
        {
            foreach (var converter in blockProcessors)
            {
                converter.Process(block);
            }
        }
    }
    
    public void ReleaseAnalysisData()
    {
        ConvertedIsil = null;
        ControlFlowGraph = null;
    }

    public override string ToString() => $"Method: {Definition?.DeclaringType!.Name}::{Definition?.Name ?? "No definition"}";

    #region StableNameDot implementation

    public ITypeInfoProvider ReturnType =>
        Definition!.RawReturnType!.ThisOrElementIsGenericParam()
            ? new GenericParameterTypeInfoProviderWrapper(Definition.RawReturnType!.GetGenericParamName())
            : TypeAnalysisContext.GetSndnProviderForType(AppContext, Definition!.RawReturnType);

    public IEnumerable<IParameterInfoProvider> ParameterInfoProviders => Parameters;
    public string MethodName => Name;

    public MethodAttributes MethodAttributes => Attributes;

    public MethodSemantics MethodSemantics
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
