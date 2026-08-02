namespace Cpp2IL.Core.ISIL;

public readonly record struct Immediate(long Value) : IOperand
{
    public ulong UnsignedValue => unchecked((ulong)Value);

    public override string ToString() => Value.ToString();
}
