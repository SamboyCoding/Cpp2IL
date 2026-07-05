using System;
using System.Collections.Generic;
using System.Linq;

namespace Cpp2IL.Core.Model.Contexts;

internal readonly record struct GenericArgumentList(List<TypeAnalysisContext> Arguments)
{
    public static implicit operator GenericArgumentList(List<TypeAnalysisContext> arguments) => new(arguments);
    public static implicit operator List<TypeAnalysisContext>(GenericArgumentList list) => list.Arguments;

    public bool Equals(GenericArgumentList other) => Arguments.SequenceEqual(other.Arguments);
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (var arg in Arguments)
            hash.Add(arg);
        return hash.ToHashCode();
    }
    public override string? ToString() => Arguments?.ToString();
}
