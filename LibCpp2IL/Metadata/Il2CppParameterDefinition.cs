using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata;

public class Il2CppParameterDefinition : ReadableClass, IIl2CppTokenProvider
{
    public int nameIndex;
    public uint token;
    [Version(Max = 24)] public int customAttributeIndex;
    public int typeIndex;

    public uint Token => token;

    public Il2CppType? RawType => LibCpp2IlMain.Binary?.GetType(typeIndex);

    public string? Name { get; private set; }

    public override void Read(ClassReadingBinaryReader reader)
    {
        nameIndex = reader.ReadInt32();

        //Cache name now
        var pos = reader.Position;
        Name = ((Il2CppMetadata)reader).ReadStringFromIndexNoReadLock(nameIndex);
        reader.Position = pos;

        token = reader.ReadUInt32();

        if (IsAtMost(24f))
            customAttributeIndex = reader.ReadInt32();

        typeIndex = reader.ReadInt32();
    }
}
