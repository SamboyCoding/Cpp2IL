using AsmResolver.DotNet;
using Decompiler.IL;
using Decompiler.ControlFlow;

namespace Decompiler;

/// <summary>
/// A method definition.
/// </summary>
public class Method
{
    /// <summary>
    /// The method definition.
    /// </summary>
    public MethodDefinition Definition;

    /// <summary>
    /// Parameter locations.
    /// </summary>
    public List<IOperand> Parameters;

    /// <summary>
    /// The control flow graph.
    /// </summary>
    public ControlFlowGraph ControlFlowGraph;

    /// <summary>
    /// Gets all instructions from the control flow graph.
    /// </summary>
    public List<Instruction> Instructions => ControlFlowGraph.AllInstructions;

    /// <summary>
    /// Dominance info.
    /// </summary>
    public Dominance Dominance;

    /// <summary>
    /// Decompiler warnings.
    /// </summary>
    public List<string> Warnings = [];

    /// <summary>
    /// Creates a method definition.
    /// </summary>
    public Method(MethodDefinition definition, List<Instruction>? instructions, List<IOperand> parameters)
    {
        Definition = definition;
        Parameters = parameters;
        ControlFlowGraph = ControlFlowGraph.Build(instructions ?? []);
        Dominance = Dominance.Build(ControlFlowGraph);
    }

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
