namespace LibCpp2IL.Elf;

public class ElfRelEntry : ReadableClass
{
    public const int StructSize64Bit = sizeof(ulong) * 2;
    public const int StructSize32Bit = sizeof(uint) * 2;
 
    public ulong Offset;
    public ulong Info;

    public override void Read(ClassReadingBinaryReader reader)
    {
        Offset = reader.ReadNUint();
        Info = reader.ReadNUint();
    }
}
