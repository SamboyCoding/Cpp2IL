namespace Cpp2IL.Core.ISIL;

public readonly struct IsilRegisterOperand(string registerName) : IsilOperandData
{
    public readonly string RegisterName = registerName;

    public override string ToString() => RegisterName;
}
