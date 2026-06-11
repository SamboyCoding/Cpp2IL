namespace LibCpp2IL.BinaryStructures;

public class Il2CppArrayType : ReadableClass
{
    public ulong etype;
    public byte rank;
    public byte numsizes;
    public byte numlobounds;
    public ulong sizes;
    public ulong lobounds;

    public Il2CppType ElementType => OwningContext.Binary.GetIl2CppTypeFromPointer(etype);

    public override void Read(ClassReadingBinaryReader reader)
    {
        etype = reader.ReadNUint();
        rank = reader.ReadByte();
        numsizes = reader.ReadByte();
        numlobounds = reader.ReadByte();
        sizes = reader.ReadNUint();
        lobounds = reader.ReadNUint();
    }
}
