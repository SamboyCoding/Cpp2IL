namespace LibCpp2IL.Elf
{
    public class ElfDynamicSymbol64 : IElfDynamicSymbol
    {
        //Slightly reorganized for alignment reasons.
        public uint _internalNameIndex;
        public byte _internalInfo;
        public byte _internalOther;
        public ushort _internalShndx;
        public ulong _internalValue;
        public ulong _internalSize;

        public uint NameOffset => _internalNameIndex;
        public ulong Value => _internalValue;
        public ulong Size => _internalSize;
        public byte Info => _internalInfo;
        public byte Other => _internalOther;
        public ushort Shndx => _internalShndx;
        
        public ElfDynamicSymbolType Type  => (ElfDynamicSymbolType) (Info & 0xF);
    }
}