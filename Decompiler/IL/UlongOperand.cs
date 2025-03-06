namespace Decompiler.IL;

/// <summary>
/// Ulong operand.
/// </summary>
public struct UlongOperand(ulong value) : IOperand
{
    public OperandType Type => OperandType.Ulong;

    /// <summary>
    /// The value.
    /// </summary>
    public ulong Value = value;

    public override string ToString() => Value.ToString("X");
}
