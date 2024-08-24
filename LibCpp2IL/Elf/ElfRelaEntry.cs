namespace LibCpp2IL.Elf;

public class ElfRelaEntry : ReadableClass
{
    public ulong Offset;
    public ulong Info;
    public ulong Addend;

    public ElfRelocationType Type => (ElfRelocationType)(Info & 0xFFFF_FFFF);
    public ulong Symbol => Info >> 32;

    public override void Read(ClassReadingBinaryReader reader)
    {
        Offset = reader.ReadNUint();
        Info = reader.ReadNUint();
        Addend = reader.ReadNUint();
    }
}
