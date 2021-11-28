using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata
{
    public class Il2CppParameterDefinition
    {
        public int nameIndex;
        public uint token;
        [Version(Max = 24)] public int customAttributeIndex;
        public int typeIndex;

        public Il2CppType? RawType => LibCpp2IlMain.Binary?.GetType(typeIndex);
    }
}