namespace Decompiler.IL;

/// <summary>
/// Stack offset operand.
/// </summary>
public struct StackOffset(int offset) : IOperand
{
    public OperandType Type => OperandType.StackOffset;

    /// <summary>
    /// The stack offset.
    /// </summary>
    public int Offset = offset;

    public override string ToString() => $"stack[{(Offset < 0 ? ("-" + (-Offset).ToString("X")) : Offset.ToString("X"))}]";
}
