namespace Decompiler.IL;

/// <summary>
/// IL operand interface.
/// </summary>
public interface IOperand
{
    /// <summary>
    /// Type of the operand.
    /// </summary>
    public OperandType Type { get; }
}
