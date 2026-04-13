using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata;

public class Il2CppInterfaceOffset : ReadableClass
{
    public Il2CppVariableWidthIndex<Il2CppType> typeIndex;
    public int offset;

    public Il2CppTypeReflectionData Type => LibCpp2ILUtils.GetTypeReflectionData(OwningContext.Binary.GetType(typeIndex));

    public override string ToString()
    {
        return $"InterfaceOffsetPair({typeIndex}/{Type.ToString() ?? "unknown type"} => {offset})";
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        typeIndex = Il2CppVariableWidthIndex<Il2CppType>.Read(reader);
        offset = reader.ReadInt32();
    }
}
