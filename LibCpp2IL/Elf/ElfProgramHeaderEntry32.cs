using LibCpp2IL.PE;

namespace LibCpp2IL.Elf;

public class ElfProgramHeaderEntry32 : ReadableClass, IElfProgramHeaderEntry
{
    private ElfProgramEntryType _internalType;
    private uint _internalOffsetRaw;
    private uint _internalVirtualAddr;
    private uint _internalPhysicalAddr;
    private uint _internalSizeRaw;
    private uint _internalSizeVirtual;
    private ElfProgramHeaderFlags _internalFlags; //This is here in 32-bit elf files
    private int _internalAlign;

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
        _internalOffsetRaw = reader.ReadUInt32();
        _internalVirtualAddr = reader.ReadUInt32();
        _internalPhysicalAddr = reader.ReadUInt32();
        _internalSizeRaw = reader.ReadUInt32();
        _internalSizeVirtual = reader.ReadUInt32();
        _internalFlags = (ElfProgramHeaderFlags)reader.ReadUInt32();
        _internalAlign = reader.ReadInt32();
    }
}
