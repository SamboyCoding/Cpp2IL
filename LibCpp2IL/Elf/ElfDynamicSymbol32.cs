namespace LibCpp2IL.Elf
{
    public class ElfDynamicSymbol32 : IElfDynamicSymbol
    {
        public uint _internalNameIndex;
        public uint _internalValue;
        public uint _internalSize;
        public byte _internalInfo;
        public byte _internalOther;
        public ushort _internalShndx;

        public uint NameOffset => _internalNameIndex;
        public ulong Value => _internalValue;
        public ulong Size => _internalSize;
        public byte Info => _internalInfo;
        public byte Other => _internalOther;
        public ushort Shndx => _internalShndx;
        public ElfDynamicSymbolType Type  => (ElfDynamicSymbolType) (Info & 0xF);
    }
}