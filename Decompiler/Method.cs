using AsmResolver.DotNet;
using Decompiler.IL;
using Decompiler.ControlFlow;

namespace Decompiler;

/// <summary>
/// A method definition.
/// </summary>
public class Method(
    MethodDefinition definition,
    List<Instruction>? instructions,
    List<IOperand> parameters,
    int archSize)
{
    /// <summary>
    /// The method definition.
    /// </summary>
    public MethodDefinition Definition = definition;

    /// <summary>
    /// Parameter locations.
    /// </summary>
    public List<IOperand> Parameters = parameters;

    /// <summary>
    /// Architecture size, 4 for 32 bit and 8 for 64 bit.
    /// </summary>
    public int ArchSize = archSize;

    /// <summary>
    /// The control flow graph.
    /// </summary>
    public ControlFlowGraph ControlFlowGraph = ControlFlowGraph.Build(instructions ?? []);

    /// <summary>
    /// All instructions.
    /// </summary>
    public List<Instruction> Instructions => ControlFlowGraph.AllInstructions;

    /// <summary>
    /// Decompiler warnings.
    /// </summary>
    public List<string> Warnings = [];

    /// <summary>
    /// Adds a new warning to the method.
    /// </summary>
    /// <param name="warning">The warning.</param>
    public void AddWarning(string warning)
    {
        if (!Warnings.Contains(warning))
            Warnings.Add(warning);
    }

    /// <summary>
    /// Removes all nop instructions.
    /// </summary>
    public void RemoveNops() => ControlFlowGraph.RemoveNops();

    /// <summary>
    /// Initially blocks are split by calls, this merges those blocks.
    /// </summary>
    public void MergeCallBlocks() => ControlFlowGraph.MergeCallBlocks();

    public override string ToString() => Definition.Name!;
}
