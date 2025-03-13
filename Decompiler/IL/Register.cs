namespace Decompiler.IL;

/// <summary>
/// A register operand.
/// </summary>
public struct Register(int number, string? name = null, int version = -1) : IOperand, IEquatable<Register>
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

    /// <summary>
    /// SSA version of the register.
    /// </summary>
    public int Version = version;

    /// <summary>
    /// Creates a copy of the register with different version.
    /// </summary>
    /// <param name="version">The SSA version.</param>
    /// <returns>The register.</returns>
    public Register Copy(int version = -1) => new(Number, Name, version);

    public static bool operator ==(Register left, Register right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Register left, Register right)
    {
        return !(left == right);
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Register register)
            return false;
        return Equals(register);
    }

    public bool Equals(Register other)
    {
        return Name == other.Name && Number == other.Number && Version == other.Version;
    }

    public override int GetHashCode()
    {
        return Number;
    }

    public override string ToString() => (Name ?? $"reg{Number}") + (Version == -1 ? "" : $"_v{Version}");
}
