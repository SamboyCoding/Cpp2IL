using System;
using System.Collections.Generic;
using System.Reflection;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents one method within the application. Can be analyzed to attempt to reconstruct the function body.
/// </summary>
public class MethodAnalysisContext : HasCustomAttributesAndName
{
    /// <summary>
    /// The underlying metadata for the method.
    ///
    /// Nullable iff this is a subclass.
    /// </summary>
    public readonly Il2CppMethodDefinition? Definition;

    /// <summary>
    /// The analysis context for the declaring type of this method.
    ///
    /// Null iff this is a subclass.
    /// </summary>
    public readonly TypeAnalysisContext? DeclaringType;

    /// <summary>
    /// The address of this method as defined in the underlying metadata.
    /// </summary>
    public virtual ulong UnderlyingPointer => Definition?.MethodPointer ?? throw new("Subclasses of MethodAnalysisContext should override UnderlyingPointer");

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
    public IControlFlowGraph? ControlFlowGraph;

    public List<ParameterAnalysisContext> Parameters = new();

    public virtual bool IsVoid => (Definition?.ReturnType?.ToString() ?? throw new("Subclasses of MethodAnalysisContext should override IsVoid")) == "System.Void";

    public virtual bool IsStatic => Definition?.IsStatic ?? throw new("Subclasses of MethodAnalysisContext should override IsStatic");

    protected override int CustomAttributeIndex => Definition?.customAttributeIndex ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeIndex if they have custom attributes");

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType?.DeclaringAssembly ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeAssembly if they have custom attributes");

    public override string DefaultName => Definition?.Name ?? throw new("Subclasses of MethodAnalysisContext should override DefaultName");
    
    public virtual MethodAttributes Attributes => Definition?.Attributes ?? throw new("Subclasses of MethodAnalysisContext should override Attributes");

    public TypeAnalysisContext? InjectedReturnType { get; set; }

    public int ParameterCount => Parameters.Count;

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
                RawBytes = AppContext.InstructionSet.GetRawBytesForMethod(this, false);
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

    public void Analyze()
    {
        ConvertedIsil = AppContext.InstructionSet.GetIsilFromMethod(this);
        
        if(ConvertedIsil.Count == 0)
            return; //Nothing to do, empty function
        
        //TODO Build control flow graph from ISIL

        // ControlFlowGraph = AppContext.InstructionSet.BuildGraphForMethod(this);
        //
        // if (ControlFlowGraph == null)
        //     return;
        //
        // ControlFlowGraph.Run();
        // InstructionSetIndependentNodes = AppContext.InstructionSet.ControlFlowGraphToISIL(ControlFlowGraph, this);
    }

    public override string ToString() => $"Method: {Definition?.DeclaringType!.Name}::{Definition?.Name ?? "No definition"}";
}