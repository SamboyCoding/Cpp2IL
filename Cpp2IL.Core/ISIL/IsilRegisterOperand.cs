namespace Cpp2IL.Core.ISIL;

public readonly struct IsilRegisterOperand : IsilOperandData
{
    public readonly string RegisterName;

    public IsilRegisterOperand(string registerName)
    {
        RegisterName = registerName;
    }

    public override string ToString() => RegisterName;
}