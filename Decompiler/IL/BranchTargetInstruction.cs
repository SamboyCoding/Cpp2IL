using Decompiler.ControlFlow;

namespace Decompiler.IL;

/// <summary>
/// Branch target instruction operand.
/// </summary>
public struct BranchTargetInstruction(Instruction instruction) : IOperand
{
    public OperandType Type => OperandType.BranchTargetInstruction;

    /// <summary>
    /// The target instruction.
    /// </summary>
    public Instruction Instruction = instruction;

    public override string ToString() => $"@{Instruction.Type}";
}
