namespace Cpp2IL.Core.Api;

public interface ICSharpSourceToken
{
    /// <summary>
    /// Returns a string of C# source code which would be equivalent to this token. For example, for types, this would be the type name, for generic types, the full type name with generic arguments, etc.
    /// </summary>
    public string GetCSharpSourceString();
}
