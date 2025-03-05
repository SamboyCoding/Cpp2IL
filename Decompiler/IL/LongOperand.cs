namespace Decompiler.IL;

/// <summary>
/// Long operand.
/// </summary>
public struct LongOperand(long value) : IOperand
{
    public OperandType Type => OperandType.Long;

    /// <summary>
    /// The value.
    /// </summary>
    public long Value = value;

    public override string ToString() => Value < 0 ? $"-{(-Value):X}" : Value.ToString("X");
}
