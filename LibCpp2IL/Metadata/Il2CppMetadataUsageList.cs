namespace LibCpp2IL.Metadata;

public class Il2CppMetadataUsageList : ReadableClass
{
    public uint start;
    public uint count;

    public override void Read(ClassReadingBinaryReader reader)
    {
        start = reader.ReadUInt32();
        count = reader.ReadUInt32();
    }
}
