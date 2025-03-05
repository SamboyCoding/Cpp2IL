namespace Decompiler.IL;

/// <summary>
/// Method reference operand.
/// </summary>
public class MethodOperand(Method method) : IOperand
{
    public OperandType Type => OperandType.Method;

    /// <summary>
    /// The method.
    /// </summary>
    public Method Method = method;

    public override string ToString() => Method.ToString();
}
