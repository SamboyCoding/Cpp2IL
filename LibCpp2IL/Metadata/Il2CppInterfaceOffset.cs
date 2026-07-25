using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata;

public class Il2CppInterfaceOffset : ReadableClass
{
    public Il2CppVariableWidthIndex<Il2CppType> typeIndex;
    public int offset;

    public Il2CppType Type => OwningContext.Binary.GetType(typeIndex);

    public override string ToString()
    {
        return $"InterfaceOffsetPair({typeIndex}/{Type} => {offset})";
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        typeIndex = Il2CppVariableWidthIndex<Il2CppType>.Read(reader);
        offset = reader.ReadInt32();
    }
}
