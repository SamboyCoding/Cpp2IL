namespace Cpp2IL.Decompiler.IL;

/// <summary>
/// Stack offset.
/// </summary>
public struct StackOffset(int offset)
{
    /// <summary>
    /// The stack offset.
    /// </summary>
    public int Offset = offset;

    public override string ToString() => $"stack[{(Offset < 0 ? ("-" + (-Offset).ToString("X")) : Offset.ToString("X"))}]";
}
