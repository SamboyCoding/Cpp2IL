namespace LibCpp2IL.Metadata;

public class Il2CppGlobalMetadataSectionHeader : ReadableClass
{
    public int Offset;
    public int Size; //previously called Count in older versions but it's always been length in bytes
    [Version(Min = 38)] public int Count;
    
    public bool HasCount => IsAtLeast(38);
    
    public override void Read(ClassReadingBinaryReader reader)
    {
        Offset = reader.ReadInt32();
        Size = reader.ReadInt32();
        if (IsAtLeast(38))
            Count = reader.ReadInt32();
    }
}
