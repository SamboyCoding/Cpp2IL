namespace LibCpp2IL.PE;

public class PeDirectoryEntryExport : ReadableClass
{
    public uint Characteristics;
    public uint TimeDataStamp;
    public ushort MajorVersion;
    public ushort MinorVersion;
    public uint RawAddressOfAssemblyName;
    public uint RawAddressOfAssemblyBase;
    public uint NumberOfExports;
    public uint NumberOfExportNames;
    public uint RawAddressOfExportTable;
    public uint RawAddressOfExportNameTable;
    public uint RawAddressOfExportOrdinalTable;

    public override void Read(ClassReadingBinaryReader reader)
    {
        Characteristics = reader.ReadUInt32();
        TimeDataStamp = reader.ReadUInt32();
        MajorVersion = reader.ReadUInt16();
        MinorVersion = reader.ReadUInt16();
        RawAddressOfAssemblyName = reader.ReadUInt32();
        RawAddressOfAssemblyBase = reader.ReadUInt32();
        NumberOfExports = reader.ReadUInt32();
        NumberOfExportNames = reader.ReadUInt32();
        RawAddressOfExportTable = reader.ReadUInt32();
        RawAddressOfExportNameTable = reader.ReadUInt32();
        RawAddressOfExportOrdinalTable = reader.ReadUInt32();
    }
}
