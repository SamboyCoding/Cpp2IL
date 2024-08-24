namespace LibCpp2IL.Elf;

public class ElfDynamicSymbol32 : ReadableClass, IElfDynamicSymbol
{
    public const int StructSize = 16;

    private uint _internalNameIndex;
    private uint _internalValue;
    private uint _internalSize;
    private byte _internalInfo;
    private byte _internalOther;
    private ushort _internalShndx;

    public uint NameOffset => _internalNameIndex;
    public ulong Value => _internalValue;
    public ulong Size => _internalSize;
    public byte Info => _internalInfo;
    public byte Other => _internalOther;
    public ushort Shndx => _internalShndx;
    public ElfDynamicSymbolType Type => (ElfDynamicSymbolType)(Info & 0xF);

    public override void Read(ClassReadingBinaryReader reader)
    {
        _internalNameIndex = reader.ReadUInt32();
        _internalValue = reader.ReadUInt32();
        _internalSize = reader.ReadUInt32();
        _internalInfo = reader.ReadByte();
        _internalOther = reader.ReadByte();
        _internalShndx = reader.ReadUInt16();
    }
}
