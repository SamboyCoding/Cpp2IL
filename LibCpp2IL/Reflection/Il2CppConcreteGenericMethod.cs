using LibCpp2IL.Metadata;

//Disable nullability checks - this class is initialized via reflection
#pragma warning disable 8618

namespace LibCpp2IL.Reflection
{
    public class Il2CppConcreteGenericMethod
    {
        public Il2CppMethodDefinition BaseMethod;
        public Il2CppTypeReflectionData[] GenericParams;
        public ulong GenericVariantPtr;
    }
}
#pragma warning restore 8618