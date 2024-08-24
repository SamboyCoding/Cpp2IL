namespace LibCpp2IL.Elf;

public class ElfDynamicEntry : ReadableClass
{
    public ElfDynamicType Tag;
    public ulong Value;

    public override void Read(ClassReadingBinaryReader reader)
    {
        Tag = (ElfDynamicType)reader.ReadNInt();
        Value = reader.ReadNUint();
    }
}
