namespace LibCpp2IL.NintendoSwitch
{
    public class NsoHeader
    {
        public uint Magic;
        public uint Version;
        public uint Reserved;
        public uint Flags;
        public NsoSegmentHeader TextSegment;
        public uint ModuleOffset;
        public NsoSegmentHeader RoDataSegment;
        public uint ModuleFileSize;
        public NsoSegmentHeader DataSegment;
        public uint BssSize;
        public byte[] DigestBuildID;
        public uint TextCompressedSize;
        public uint RoDataCompressedSize;
        public uint DataCompressedSize;
        public byte[] NsoHeaderReserved;
        public NsoRelativeExtent APIInfo;
        public NsoRelativeExtent DynStr;
        public NsoRelativeExtent DynSym;
        public byte[] TextHash;
        public byte[] RoDataHash;
        public byte[] DataHash;
    }
}