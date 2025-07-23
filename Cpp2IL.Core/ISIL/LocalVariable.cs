using System;
using System.Text;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ISIL;

public class LocalVariable(string name, Register register, TypeAnalysisContext? type) : IEquatable<LocalVariable>
{
    public string Name = name;
    public Register Register = register;

    /// <summary>
    /// null if typeprop has not been done yet.
    /// </summary>
    public TypeAnalysisContext? Type = type;

    public bool IsThis = false;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Name);
        if (Type != null)
            sb.Append($" ({Type.Name})");
        return sb.ToString();
    }

    public static bool operator ==(LocalVariable left, LocalVariable right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LocalVariable left, LocalVariable right)
    {
        return !(left == right);
    }

    public override bool Equals(object? obj)
    {
        if (obj is not LocalVariable local)
            return false;
        return Equals(local);
    }

    public bool Equals(LocalVariable other)
    {
        return Name == other.Name;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name);
    }
}
