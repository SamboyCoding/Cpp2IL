namespace Cpp2IL.Core.ISIL;
public readonly struct InstructionSpecificInfo<T> : IsilOperandData where T: notnull
{
    public readonly T Info;

    public InstructionSpecificInfo(in T info)
    {
        Info = info;
    }

    public override string? ToString() => Info.ToString();
}
