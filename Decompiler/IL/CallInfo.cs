using AsmResolver.DotNet;

namespace Decompiler.IL;

/// <summary>
/// Method call info operand.
/// </summary>
public struct CallInfo(MethodDefinition method, List<IOperand> parameters) : IOperand
{
    public OperandType Type => OperandType.CallInfo;

    /// <summary>
    /// The method.
    /// </summary>
    public MethodDefinition Method = method;

    /// <summary>
    /// All parameters (including this).
    /// </summary>
    public List<IOperand> Parameters = parameters;

    public override string ToString() => $"{Method!.DeclaringType!.FullName}.{Method.Name}({string.Join(", ", Parameters)})";
}
