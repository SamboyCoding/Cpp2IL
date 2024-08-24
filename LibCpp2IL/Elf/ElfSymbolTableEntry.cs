namespace LibCpp2IL.Elf;

public class ElfSymbolTableEntry
{
    public enum ElfSymbolEntryType
    {
        Function,
        Name,
        Import,
        Unknown
    }

    public string Name = null!;
    public ElfSymbolEntryType Type;
    public ulong VirtualAddress;
}
