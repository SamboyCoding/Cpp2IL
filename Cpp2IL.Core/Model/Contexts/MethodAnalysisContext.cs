using System;
using System.Collections.Generic;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents one method within the application. Can be analyzed to attempt to reconstruct the function body.
/// </summary>
public class MethodAnalysisContext : HasCustomAttributes
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
    public byte[] RawBytes;
    
    /// <summary>
    /// The control flow graph for this method, if one is built.
    /// </summary>
    public IControlFlowGraph? ControlFlowGraph;
    
    /// <summary>
    /// The first-stage-analyzed nodes containing ISIL instructions.
    /// </summary>
    public List<InstructionSetIndependentNode>? InstructionSetIndependentNodes;
    
    public virtual Il2CppParameterReflectionData[] Parameters => Definition?.Parameters ?? throw new("Subclasses of MethodAnalysisContext should override Parameters");

    public virtual bool IsVoid => Definition?.ReturnType is {isType: true, isGenericType: false, isArray: false} returnType ? returnType.baseType!.FullName == "System.Void" : throw new("Subclasses of MethodAnalysisContext should override IsVoid");
    
    public virtual bool IsStatic => Definition?.IsStatic ?? throw new("Subclasses of MethodAnalysisContext should override IsStatic");
    
    protected override int CustomAttributeIndex => Definition?.customAttributeIndex ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeIndex if they have custom attributes");

    protected override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType?.DeclaringAssembly ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeAssembly if they have custom attributes");

    public override string CustomAttributeOwnerName => Definition?.Name ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeOwnerName if they have custom attributes");

    public MethodAnalysisContext(Il2CppMethodDefinition definition, TypeAnalysisContext parent) : base(definition.token, parent.AppContext)
    {
        DeclaringType = parent;
        Definition = definition;
        
        InitCustomAttributeData();

        if (Definition.MethodPointer != 0)
            RawBytes = AppContext.InstructionSet.GetRawBytesForMethod(this, false);
        else
            RawBytes = Array.Empty<byte>();
    }

    protected MethodAnalysisContext(ApplicationAnalysisContext context) : base(0, context)
    {
        RawBytes = Array.Empty<byte>();
    }

    public void Analyze()
    {
        ControlFlowGraph = AppContext.InstructionSet.BuildGraphForMethod(this);
        
        if(ControlFlowGraph == null)
            return;
        
        ControlFlowGraph.Run();
        InstructionSetIndependentNodes = AppContext.InstructionSet.ControlFlowGraphToISIL(ControlFlowGraph, this);
    }
    
    public override string ToString() => $"Method: {Definition?.DeclaringType!.Name}::{Definition?.Name ?? "No definition"}";
}