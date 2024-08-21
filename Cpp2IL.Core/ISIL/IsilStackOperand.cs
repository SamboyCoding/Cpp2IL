namespace Cpp2IL.Core.ISIL;

public readonly struct IsilStackOperand(int offset) : IsilOperandData
{
    public readonly int Offset = offset;

    public override string ToString() => $"stack:0x{Offset:X}";
}