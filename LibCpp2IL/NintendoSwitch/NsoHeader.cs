using System;

namespace LibCpp2IL.NintendoSwitch;

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
    public byte[] DigestBuildId = [];
    public uint TextCompressedSize;
    public uint RoDataCompressedSize;
    public uint DataCompressedSize;
    public byte[] NsoHeaderReserved = [];
    public NsoRelativeExtent ApiInfo = null!;
    public NsoRelativeExtent DynStr = null!;
    public NsoRelativeExtent DynSym = null!;
    public byte[] TextHash = [];
    public byte[] RoDataHash = [];
    public byte[] DataHash = [];
}
