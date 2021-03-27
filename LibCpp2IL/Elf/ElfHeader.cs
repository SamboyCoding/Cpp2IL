namespace LibCpp2IL.Elf
{
    public class ElfFileHeader
    {
        public ElfFileType Type;
        public short Machine; //3 => x86, 0x3e => x86_64, 0x28 => ARM (v7, 32-bit), 0xB7 => ARM64 (v8, 64-bit)
        public int Version; //Should be 1
        public long pEntryPoint; //arch-dependent length, but ClassReadingBinaryReader handles it.
        public long pProgramHeader; //arch-dependent length
        public long pSectionHeader; //arch-dependent length
        public int Flags;
        public short HeaderSize; //Meta!
        
        public short ProgramHeaderEntrySize;
        public short ProgramHeaderEntryCount;

        public short SectionHeaderEntrySize;
        public short SectionHeaderEntryCount;

        public short SectionNameSectionOffset; //Offset in the Section Header of the Section containing the names of all the Sections.
    }
}