using System;

namespace LibCpp2IL.NintendoSwitch
{
    public class NsoHeader
    {
        public uint Magic;
        public uint Version;
        public uint Reserved;
        public uint Flags;
        public NsoSegmentHeader TextSegment = null!;
        public uint ModuleOffset;
        public NsoSegmentHeader RoDataSegment = null!;
        public uint ModuleFileSize;
        public NsoSegmentHeader DataSegment = null!;
        public uint BssSize;
        public byte[] DigestBuildId = Array.Empty<byte>();
        public uint TextCompressedSize;
        public uint RoDataCompressedSize;
        public uint DataCompressedSize;
        public byte[] NsoHeaderReserved = Array.Empty<byte>();
        public NsoRelativeExtent ApiInfo = null!;
        public NsoRelativeExtent DynStr = null!;
        public NsoRelativeExtent DynSym = null!;
        public byte[] TextHash = Array.Empty<byte>();
        public byte[] RoDataHash = Array.Empty<byte>();
        public byte[] DataHash = Array.Empty<byte>();
    }
}