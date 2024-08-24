namespace LibCpp2IL.Elf;

public interface IElfDynamicSymbol
{
    public uint NameOffset { get; }
    public ulong Value { get; }
    public ulong Size { get; }
    public byte Info { get; }
    public byte Other { get; }
    public ushort Shndx { get; }

    public ElfDynamicSymbolType Type { get; }
}
