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
    /// Gets all instructions from the control flow graph.
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

    public override string ToString() => Definition.Name!;
}
