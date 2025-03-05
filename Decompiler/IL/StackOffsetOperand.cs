namespace Decompiler.IL;

/// <summary>
/// Stack offset operand.
/// </summary>
public struct StackOffsetOperand(int offset) : IOperand
{
    public OperandType Type => OperandType.StackOffset;

    /// <summary>
    /// The stack offset.
    /// </summary>
    public int Offset = offset;

    public override string ToString() => $"stack[{Offset:X}]";
}
