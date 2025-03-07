namespace Decompiler.IL;

/// <summary>
/// A single instruction.
/// </summary>
public class Instruction(int index, OpCode opcode, params IOperand?[] operands) : IOperand
{
    /// <summary>
    /// Index of the instruction.
    /// </summary>
    public int Index = index;

    /// <summary>
    /// Opcode of the instruction.
    /// </summary>
    public OpCode OpCode = opcode;

    /// <summary>
    /// Operands for the instruction.
    /// </summary>
    public List<IOperand?> Operands = operands.ToList();

    /// <summary>
    /// True if the instruction doesn't affect control flow.
    /// </summary>
    public bool IsFallThrough => OpCode != OpCode.Return && OpCode != OpCode.Jump && OpCode != OpCode.ConditionalJump;

    public OperandType Type => OperandType.Instruction;

    public override string ToString() =>
        $"({Index} {OpCode} {string.Join(", ", Operands.Select(o => o == null ? "null" : o.ToString()))})";
}
