namespace LibCpp2IL.Elf
{
    public class ElfSymbolTableEntry
    {
        public enum ElfSymbolEntryType
        {
            FUNCTION,
            NAME,
            IMPORT,
            UNKNOWN
        }

        public string Name;
        public ElfSymbolEntryType Type;
        public ulong VirtualAddress;
    }
}