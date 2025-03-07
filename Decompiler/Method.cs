using AsmResolver.DotNet;
using Decompiler.IL;
using Decompiler.ControlFlow;

namespace Decompiler;

/// <summary>
/// A method definition.
/// </summary>
public class Method(MethodDefinition definition, List<Instruction>? instructions, List<IOperand> parameters)
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
    /// The control flow graph.
    /// </summary>
    public ControlFlowGraph ControlFlowGraph = ControlFlowGraph.Build(instructions ?? []);

    /// <summary>
    /// All instructions.
    /// </summary>
    public List<Instruction> Instructions => ControlFlowGraph.AllInstructions;

    public override string ToString() => Definition.Name!;
}
