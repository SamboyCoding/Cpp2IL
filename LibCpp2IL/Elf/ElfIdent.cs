namespace LibCpp2IL.Elf;

public class ElfFileIdent : ReadableClass
{
    public int Magic;
    public byte Architecture; //1 => 32-bit, 2 => 64-bit
    public byte Endianness; //1 => LE, 2 => BE
    public byte Version; //Must be 1
    public byte OSAbi; //Probably ignore.
    public byte AbiVersion;
    //7 bytes of padding here.

    public override void Read(ClassReadingBinaryReader reader)
    {
        Magic = reader.ReadInt32();
        Architecture = reader.ReadByte();
        Endianness = reader.ReadByte();
        Version = reader.ReadByte();
        OSAbi = reader.ReadByte();
        AbiVersion = reader.ReadByte();
        reader.ReadBytes(7);
    }
}
