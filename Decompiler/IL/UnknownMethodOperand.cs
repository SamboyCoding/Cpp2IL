namespace Decompiler.IL;

/// <summary>
/// Method that was not found.
/// </summary>
public struct UnknownMethodOperand(string method) : IOperand
{
    public OperandType Type => OperandType.UnknownMethod;

    /// <summary>
    /// The method.
    /// </summary>
    public string Method = method;

    public override string ToString() => Method;
}
