namespace Cpp2IL.Core.ISIL;

public readonly record struct StringLiteral(string Value) : IOperand
{
    public override string ToString() => $"\"{Value}\"";
}
