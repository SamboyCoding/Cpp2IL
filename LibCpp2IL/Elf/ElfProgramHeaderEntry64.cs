using LibCpp2IL.PE;

namespace LibCpp2IL.Elf
{
    public class ElfProgramHeaderEntry64 : IElfProgramHeaderEntry
    {
        public ElfProgramEntryType _internalType;
        public ElfProgramHeaderFlags _internalFlags; //This is here in 64-bit elf files.
        public ulong _internalOffsetRaw;
        public ulong _internalVirtualAddr;
        public ulong _internalPhysicalAddr;
        public ulong _internalSizeRaw;
        public ulong _internalSizeVirtual;
        public long _internalAlign;

        public ElfProgramHeaderFlags Flags => _internalFlags;
        public ElfProgramEntryType Type => _internalType;
        public ulong RawAddress => _internalOffsetRaw;
        public ulong VirtualAddress => _internalVirtualAddr;
        public ulong PhysicalAddr => _internalPhysicalAddr;
        public ulong RawSize => _internalSizeRaw;
        public ulong VirtualSize => _internalSizeVirtual;
        public long Align => _internalAlign;
    }
}