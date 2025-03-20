namespace LibCpp2IL.MachO;

public class MachOUniversalHeader : ReadableClass
{
    public uint Magic; //0xCAFEBABE
    public uint NumberFileEntries; // Number of file entries in the container

    public override void Read(ClassReadingBinaryReader reader)
    {
        Magic = reader.ReadUInt32WithReversedBits();
        NumberFileEntries = reader.ReadUInt32WithReversedBits();
    }
}
