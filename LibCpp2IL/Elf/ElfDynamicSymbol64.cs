namespace LibCpp2IL.Elf;

public class ElfDynamicSymbol64 : ReadableClass, IElfDynamicSymbol
{
    public const int StructSize = 24;

    //Slightly reorganized for alignment reasons.
    private uint _internalNameIndex;
    private byte _internalInfo;
    private byte _internalOther;
    private ushort _internalShndx;
    private ulong _internalValue;
    private ulong _internalSize;

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
        _internalInfo = reader.ReadByte();
        _internalOther = reader.ReadByte();
        _internalShndx = reader.ReadUInt16();
        _internalValue = reader.ReadUInt64();
        _internalSize = reader.ReadUInt64();
    }
}
