using AsmResolver.DotNet;
using Cpp2IL.Decompiler.ControlFlow;
using Cpp2IL.Decompiler.IL;

namespace Cpp2IL.Decompiler;

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
    public List<object> Parameters;

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
    /// Local variables.
    /// </summary>
    public List<LocalVariable> Locals = [];

    /// <summary>
    /// Parameter locals.
    /// </summary>
    public List<LocalVariable> ParameterLocals = [];

    /// <summary>
    /// Decompiler warnings.
    /// </summary>
    public List<string> Warnings = [];

    /// <summary>
    /// Creates a method definition.
    /// </summary>
    public Method(MethodDefinition definition, List<Instruction>? instructions, List<object> parameters)
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

    /// <summary>
    /// If the local is not a parameter, remove it.
    /// </summary>
    /// <param name="local">The local.</param>
    public void TryRemoveLocal(LocalVariable local)
    {
        if (!ParameterLocals.Contains(local))
            Locals.Remove(local);
    }

    public override string ToString() => Definition.Name!;
}
