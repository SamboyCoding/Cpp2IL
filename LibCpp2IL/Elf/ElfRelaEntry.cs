namespace LibCpp2IL.Elf
{
    public class ElfRelaEntry
    {
        public ulong Offset;
        public ulong Info;
        public ulong Addend;

        public ElfRelocationType Type => (ElfRelocationType) (Info & 0xFFFF_FFFF);
        public ulong Symbol => Info >> 32;
    }
}