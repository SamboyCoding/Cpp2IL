using AsmResolver.DotNet.Signatures;

namespace Decompiler.IL;

/// <summary>
/// Local variable operand.
/// </summary>
public class LocalVariable(string name, Register register, TypeSignature? localType = null) : IOperand
{
    public OperandType Type => OperandType.Local;

    /// <summary>
    /// Name of the variable.
    /// </summary>
    public string Name = name;

    /// <summary>
    /// Location of the variable.
    /// </summary>
    public Register Register = register;

    /// <summary>
    /// Type of the variable.
    /// </summary>
    public TypeSignature? LocalType = localType;

    /// <summary>
    /// Is this the 'this' parameter?
    /// </summary>
    public bool IsThis = false;

    public override string ToString() => $"{Name}:{LocalType?.Name ?? "??"}";
}
