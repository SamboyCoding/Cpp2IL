namespace Decompiler.IL;

/// <summary>
/// Branch target operand.
/// </summary>
public class BranchTarget(Instruction instruction) : IOperand
{
    public OperandType Type => OperandType.BranchTarget;

    /// <summary>
    /// The target instruction.
    /// </summary>
    public Instruction Instruction = instruction;

    public override string ToString() => $"@{Instruction}";
}
