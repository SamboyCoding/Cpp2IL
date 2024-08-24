namespace LibCpp2IL.Elf;

public class ElfFileHeader : ReadableClass
{
    public ElfFileType Type;
    public short Machine; //3 => x86, 0x3e => x86_64, 0x28 => ARM (v7, 32-bit), 0xB7 => ARM64 (v8, 64-bit)
    public int Version; //Should be 1
    public long pEntryPoint; //arch-dependent length
    public long pProgramHeader; //arch-dependent length
    public long pSectionHeader; //arch-dependent length
    public int Flags;
    public short HeaderSize; //Meta!

    public short ProgramHeaderEntrySize;
    public short ProgramHeaderEntryCount;

    public short SectionHeaderEntrySize;
    public short SectionHeaderEntryCount;

    public short SectionNameSectionOffset; //Offset in the Section Header of the Section containing the names of all the Sections.

    public override void Read(ClassReadingBinaryReader reader)
    {
        Type = (ElfFileType)reader.ReadInt16();
        Machine = reader.ReadInt16();
        Version = reader.ReadInt32();
        pEntryPoint = reader.ReadNInt();
        pProgramHeader = reader.ReadNInt();
        pSectionHeader = reader.ReadNInt();
        Flags = reader.ReadInt32();
        HeaderSize = reader.ReadInt16();
        ProgramHeaderEntrySize = reader.ReadInt16();
        ProgramHeaderEntryCount = reader.ReadInt16();
        SectionHeaderEntrySize = reader.ReadInt16();
        SectionHeaderEntryCount = reader.ReadInt16();
        SectionNameSectionOffset = reader.ReadInt16();
    }
}
