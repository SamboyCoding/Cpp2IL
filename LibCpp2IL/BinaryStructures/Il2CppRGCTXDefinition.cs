using LibCpp2IL.Reflection;

namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppRGCTXDefinition
    {
        public Il2CppRGCTXDataType type;
        public int _rawIndex;

        public int MethodIndex => _rawIndex;

        public int TypeIndex => _rawIndex;

        public Il2CppMethodSpec? MethodSpec => LibCpp2IlMain.Binary?.GetMethodSpec(MethodIndex);

        public Il2CppTypeReflectionData? Type => LibCpp2ILUtils.GetTypeReflectionData(LibCpp2IlMain.Binary!.GetType(TypeIndex));
    }
}