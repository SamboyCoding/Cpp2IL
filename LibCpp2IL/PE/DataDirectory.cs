namespace LibCpp2IL.PE;

public class DataDirectory : ReadableClass
{
    public uint VirtualAddress;
    public uint Size;

    public override void Read(ClassReadingBinaryReader reader)
    {
        VirtualAddress = reader.ReadUInt32();
        Size = reader.ReadUInt32();
    }
}
