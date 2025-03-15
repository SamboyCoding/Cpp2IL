using AsmResolver.DotNet;

namespace Decompiler.IL;

/// <summary>
/// A method reference operand.
/// </summary>
public class MethodOperand(MethodDefinition method) : IOperand
{
    public OperandType Type => OperandType.Method;

    /// <summary>
    /// The method.
    /// </summary>
    public MethodDefinition Method = method;

    /// <summary>
    /// Is the method a constructor?
    /// </summary>
    public bool IsConstructor => Method.IsConstructor;

    public override string ToString() => Method.DeclaringType!.Name! + "." + Method.Name;
}
