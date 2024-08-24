namespace LibCpp2IL.NintendoSwitch;

public class NsoModHeader
{
    public uint ModOffset;
    public uint DynamicOffset;
    public uint BssStart;
    public uint BssEnd;
    public uint EhFrameHdrStart;
    public uint EhFrameHdrEnd;

    public NsoSegmentHeader BssSegment = null!;
}
