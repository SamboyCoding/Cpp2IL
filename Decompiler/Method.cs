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
    /// Parameter locations (if not static, first is 'this').
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
    /// All locals (including params), this can be empty at early analysis stages.
    /// </summary>
    public List<LocalVariable> Locals = [];

    /// <summary>
    /// Parameter local variables.
    /// </summary>
    public List<LocalVariable> ParameterLocals = [];

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

    /// <summary>
    /// Gets the name of i-th parameter.
    /// </summary>
    /// <param name="i">The index.</param>
    /// <returns>The parameter name.</returns>
    public string GetParameterName(int i) => Definition.Parameters[i].Name;

    /// <summary>
    /// Gets the return local.
    /// </summary>
    /// <returns>The return local.</returns>
    public LocalVariable? GetReturnLocal() => (LocalVariable?)(Instructions.FirstOrDefault(i => i is { OpCode: OpCode.Return, Operands.Count: 1 })?.Operands[0]);

    public override string ToString() => Definition.Name!;
}
