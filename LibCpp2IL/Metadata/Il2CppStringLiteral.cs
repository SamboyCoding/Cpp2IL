namespace LibCpp2IL.Metadata;

public class Il2CppStringLiteral : ReadableClass
{
    public uint length;
    public int dataIndex;

    public override void Read(ClassReadingBinaryReader reader)
    {
        length = reader.ReadUInt32();
        dataIndex = reader.ReadInt32();
    }
}
