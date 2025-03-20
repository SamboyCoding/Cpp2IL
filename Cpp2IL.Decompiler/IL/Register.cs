namespace Cpp2IL.Decompiler.IL;

/// <summary>
/// A register.
/// </summary>
public struct Register(int number, string? name = null, int version = -1) : IEquatable<Register>
{
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

    public override string ToString() => (Name ?? $"reg{Number}") + (Version == -1 ? "" : $"_v{Version}");

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

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = Number;
            hashCode = (hashCode * 397) ^ (Name != null ? Name.GetHashCode() : 0);
            hashCode = (hashCode * 397) ^ Version;
            return hashCode;
        }
    }

    public bool Equals(Register other)
    {
        return Name == other.Name && Number == other.Number && Version == other.Version;
    }
}
