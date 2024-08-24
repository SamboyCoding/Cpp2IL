using LibCpp2IL.PE;

namespace LibCpp2IL.Elf;

public class ElfProgramHeaderEntry64 : ReadableClass, IElfProgramHeaderEntry
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

    public override void Read(ClassReadingBinaryReader reader)
    {
        _internalType = (ElfProgramEntryType)reader.ReadUInt32();
        _internalFlags = (ElfProgramHeaderFlags)reader.ReadUInt32();
        _internalOffsetRaw = reader.ReadUInt64();
        _internalVirtualAddr = reader.ReadUInt64();
        _internalPhysicalAddr = reader.ReadUInt64();
        _internalSizeRaw = reader.ReadUInt64();
        _internalSizeVirtual = reader.ReadUInt64();
        _internalAlign = reader.ReadInt64();
    }
}
