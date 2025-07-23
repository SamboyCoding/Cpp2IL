using System;

namespace Cpp2IL.Core.ISIL;

public struct Register : IEquatable<Register>
{
    public Register(int? number, string? name, int version = -1)
    {
        if (number == null && name == null)
            throw new ArgumentException("Either number or name must be provided, not both null.");

        if (number == null)
        {
            Name = name!;
            Number = name!.GetHashCode();
        }
        else if (name == null)
        {
            Name = "reg" + number;
            Number = (int)number;
        }
        else
        {
            Name = name;
            Number = (int)number;
        }

        Version = version;
    }

    public int Number;
    public string Name;

    /// <summary>
    /// SSA version of the register, -1 = not in SSA form.
    /// </summary>
    public int Version;

    /// <summary>
    /// Creates a copy of the register with different version.
    /// </summary>
    /// <param name="version">The SSA version.</param>
    /// <returns>The register.</returns>
    public Register Copy(int version = -1) => new(Number, Name, version);

    public override string ToString() => Name + (Version == -1 ? "" : $"_v{Version}");

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
