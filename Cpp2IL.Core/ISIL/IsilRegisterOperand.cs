namespace Cpp2IL.Core.ISIL;

public class IsilRegisterOperand : IsilOperandData
{
    public string RegisterName;

    public IsilRegisterOperand(string registerName)
    {
        RegisterName = registerName;
    }
}