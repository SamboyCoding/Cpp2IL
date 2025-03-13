namespace Decompiler.IL;

/// <summary>
/// String operand.
/// </summary>
public struct StringOp(string text) : IOperand
{
    public OperandType Type => OperandType.String;

    /// <summary>
    /// The text.
    /// </summary>
    public string Text = text;

    public override string ToString() => $"\"{Text}\"";
}
