namespace Cpp2IL.Decompiler;

/// <summary>
/// Complexity limit reached exception.
/// </summary>
public class LimitReachedException : Exception
{
    public LimitReachedException() { }
    public LimitReachedException(string message) : base(message) { }
    public LimitReachedException(string message, Exception inner) : base(message, inner) { }
}
