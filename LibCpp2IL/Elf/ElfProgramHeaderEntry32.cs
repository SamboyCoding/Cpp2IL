using LibCpp2IL.PE;

namespace LibCpp2IL.Elf
{
    public class ElfProgramHeaderEntry32 : IElfProgramHeaderEntry
    {
        public ElfProgramEntryType _internalType;
        public uint _internalOffsetRaw;
        public uint _internalVirtualAddr;
        public uint _internalPhysicalAddr;
        public uint _internalSizeRaw;
        public uint _internalSizeVirtual;
        public ElfProgramHeaderFlags _internalFlags; //This is here in 32-bit elf files
        public int _internalAlign;

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