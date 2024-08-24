namespace LibCpp2IL.Elf;

public class ElfRelEntry : ReadableClass
{
    public ulong Offset;
    public ulong Info;

    public override void Read(ClassReadingBinaryReader reader)
    {
        Offset = reader.ReadNUint();
        Info = reader.ReadNUint();
    }
}
