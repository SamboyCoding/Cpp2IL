namespace Decompiler.IL;

/// <summary>
/// Int operand.
/// </summary>
public struct IntOperand(int value) : IOperand
{
    public OperandType Type => OperandType.Int;

    /// <summary>
    /// The value.
    /// </summary>
    public int Value = value;

    public override string ToString() => Value.ToString();
}
