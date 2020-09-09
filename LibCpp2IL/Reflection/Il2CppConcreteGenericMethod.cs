using System.Reflection;
using LibCpp2IL.Metadata;

namespace LibCpp2IL.Reflection
{
    public class Il2CppConcreteGenericMethod
    {
        public Il2CppMethodDefinition BaseMethod;
        public Il2CppTypeReflectionData[] GenericParams;
        public ulong GenericVariantPtr;
    }
}