namespace Cpp2IL.Core;

public class DecompilerException : System.Exception
{
    public DecompilerException() { }
    public DecompilerException(string message) : base("Decompilation failed: " + message) { }
    public DecompilerException(string message, System.Exception inner) : base("Decompilation failed: " + message, inner) { }
}
