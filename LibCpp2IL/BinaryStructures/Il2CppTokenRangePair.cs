namespace LibCpp2IL.BinaryStructures;

public class Il2CppTokenRangePair : ReadableClass
{
    public int token;
    public int start;
    public int length;

    public override void Read(ClassReadingBinaryReader reader)
    {
        token = reader.ReadInt32();
        start = reader.ReadInt32();
        length = reader.ReadInt32();
    }
}
