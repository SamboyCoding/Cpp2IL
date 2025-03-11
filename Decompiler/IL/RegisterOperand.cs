namespace Decompiler.IL;

/// <summary>
/// A register operand.
/// </summary>
public struct RegisterOperand(int number, string? name = null) : IOperand
{
    public OperandType Type => OperandType.Register;

    /// <summary>
    /// The register number.
    /// </summary>
    public int Number = number;

    /// <summary>
    /// Name of the register, only the number will be printed if this is null.
    /// </summary>
    public string? Name = name;

    public override string ToString() => Name ?? $"reg{Number}";
}
