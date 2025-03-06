using AsmResolver.DotNet;

namespace Decompiler.IL;

/// <summary>
/// Method reference operand.
/// </summary>
public class MethodOperand(MethodDefinition method) : IOperand
{
    public OperandType Type => OperandType.Method;

    /// <summary>
    /// The method.
    /// </summary>
    public MethodDefinition Method = method;

    public override string ToString() => Method.DeclaringType!.FullName + "." + Method.Name;
}
