namespace Cpp2IL.PE
{
    public class PeDirectoryEntryExport
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
    }
}