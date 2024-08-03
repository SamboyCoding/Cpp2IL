namespace Cpp2IL.Core.Extensions;

public static class StringExtensions
{
    public static string EscapeString(this string str)
        => str
            .Replace("\\", @"\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            .Replace("\0", "\\0");
}
