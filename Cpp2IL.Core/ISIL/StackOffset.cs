namespace Cpp2IL.Core.ISIL;

public struct StackOffset(int offset)
{
    public int Offset = offset;

    public override string ToString() => $"stack[{(Offset < 0 ? ("-" + (-Offset).ToString("X")) : Offset.ToString("X"))}]";
}
