#pragma warning disable 8618
//Disable null check because this stuff is initialized by reflection
namespace LibCpp2IL.PE;

public class OptionalHeader64 : ReadableClass
{
    public ushort Magic;
    public byte MajorLinkerVersion;
    public byte MinorLinkerVersion;
    public uint SizeOfCode;
    public uint SizeOfInitializedData;
    public uint SizeOfUninitializedData;
    public uint AddressOfEntryPoint;
    public uint BaseOfCode;
    public ulong ImageBase;
    public uint SectionAlignment;
    public uint FileAlignment;
    public ushort MajorOperatingSystemVersion;
    public ushort MinorOperatingSystemVersion;
    public ushort MajorImageVersion;
    public ushort MinorImageVersion;
    public ushort MajorSubsystemVersion;
    public ushort MinorSubsystemVersion;
    public uint Win32VersionValue;
    public uint SizeOfImage;
    public uint SizeOfHeaders;
    public uint CheckSum;
    public ushort Subsystem;
    public ushort DllCharacteristics;
    public ulong SizeOfStackReserve;
    public ulong SizeOfStackCommit;
    public ulong SizeOfHeapReserve;
    public ulong SizeOfHeapCommit;
    public uint LoaderFlags;

    public uint NumberOfRvaAndSizes;
    public DataDirectory[] DataDirectory { get; set; }

    public override void Read(ClassReadingBinaryReader reader)
    {
        Magic = reader.ReadUInt16();
        MajorLinkerVersion = reader.ReadByte();
        MinorLinkerVersion = reader.ReadByte();
        SizeOfCode = reader.ReadUInt32();
        SizeOfInitializedData = reader.ReadUInt32();
        SizeOfUninitializedData = reader.ReadUInt32();
        AddressOfEntryPoint = reader.ReadUInt32();
        BaseOfCode = reader.ReadUInt32();
        ImageBase = reader.ReadUInt64();
        SectionAlignment = reader.ReadUInt32();
        FileAlignment = reader.ReadUInt32();
        MajorOperatingSystemVersion = reader.ReadUInt16();
        MinorOperatingSystemVersion = reader.ReadUInt16();
        MajorImageVersion = reader.ReadUInt16();
        MinorImageVersion = reader.ReadUInt16();
        MajorSubsystemVersion = reader.ReadUInt16();
        MinorSubsystemVersion = reader.ReadUInt16();
        Win32VersionValue = reader.ReadUInt32();
        SizeOfImage = reader.ReadUInt32();
        SizeOfHeaders = reader.ReadUInt32();
        CheckSum = reader.ReadUInt32();
        Subsystem = reader.ReadUInt16();
        DllCharacteristics = reader.ReadUInt16();
        SizeOfStackReserve = reader.ReadUInt64();
        SizeOfStackCommit = reader.ReadUInt64();
        SizeOfHeapReserve = reader.ReadUInt64();
        SizeOfHeapCommit = reader.ReadUInt64();
        LoaderFlags = reader.ReadUInt32();
        NumberOfRvaAndSizes = reader.ReadUInt32();

        DataDirectory = new DataDirectory[NumberOfRvaAndSizes];

        for (var i = 0; i < NumberOfRvaAndSizes; i++)
        {
            DataDirectory[i] = reader.ReadReadableHereNoLock<DataDirectory>();
        }
    }
}
