namespace LibCpp2IL.Elf
{
    public class ElfSectionHeaderEntry
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
        
        public string Name { get; set; }
    }
}