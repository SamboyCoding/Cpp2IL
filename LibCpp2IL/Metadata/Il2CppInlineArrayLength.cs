using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata;

public class Il2CppInlineArrayLength : ReadableClass
{
    public Il2CppVariableWidthIndex<Il2CppType> typeIndex;
    public int length;
    
    public override void Read(ClassReadingBinaryReader reader)
    {
        typeIndex = Il2CppVariableWidthIndex<Il2CppType>.Read(reader);
        length = reader.ReadInt32();
    }
}
