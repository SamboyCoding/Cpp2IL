using System;
using Cpp2IL.Core.Graphs;
using LibCpp2IL.Metadata;

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

    public MethodAnalysisContext(Il2CppMethodDefinition definition, TypeAnalysisContext parent) : base(parent.AppContext)
    {
        DeclaringType = parent;
        Definition = definition;
        RawBytes = AppContext.InstructionSet.GetRawBytesForMethod(this, false);
    }

    protected MethodAnalysisContext(ApplicationAnalysisContext context) : base(context)
    {
        RawBytes = Array.Empty<byte>();
    }

    public void Analyze()
    {
        ControlFlowGraph = AppContext.InstructionSet.BuildGraphForMethod(this);
    }
}