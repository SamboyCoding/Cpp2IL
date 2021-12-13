namespace Cpp2IL.Core.ISIL;

public class IsilStackOperand : IsilOperandData
{
    public readonly int Offset;

    public IsilStackOperand(int offset)
    {
        Offset = offset;
    }

    public override string ToString() => $"stack:0x{Offset:X}";
}