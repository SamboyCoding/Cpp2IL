namespace LibCpp2IL.Elf;

public class ElfSectionHeaderEntry : ReadableClass
{
    public uint NameOffset;
    public ElfSectionEntryType Type;
    public ElfSectionHeaderFlags Flags;
    public ulong VirtualAddress; //Address
    public ulong RawAddress; //Offset
    public ulong Size;
    public int LinkedSectionIndex;
    public int SectionInfo;
    public long Alignment;
    public long EntrySize;

    public string? Name { get; set; }

    public override void Read(ClassReadingBinaryReader reader)
    {
        NameOffset = reader.ReadUInt32();
        Type = (ElfSectionEntryType)reader.ReadUInt32();
        Flags = (ElfSectionHeaderFlags)reader.ReadNInt();
        VirtualAddress = reader.ReadNUint();
        RawAddress = reader.ReadNUint();
        Size = reader.ReadNUint();
        LinkedSectionIndex = reader.ReadInt32();
        SectionInfo = reader.ReadInt32();
        Alignment = reader.ReadNInt();
        EntrySize = reader.ReadNInt();
    }
}
