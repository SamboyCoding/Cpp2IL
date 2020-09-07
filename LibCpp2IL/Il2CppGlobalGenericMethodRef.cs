using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
#pragma warning disable 8618

namespace LibCpp2IL
{
    public class Il2CppGlobalGenericMethodRef
    {
        public Il2CppTypeDefinition declaringType;
        public Il2CppTypeReflectionData[] typeGenericParams;
        public Il2CppMethodDefinition baseMethod;
        public Il2CppTypeReflectionData[] methodGenericParams;
    }
}