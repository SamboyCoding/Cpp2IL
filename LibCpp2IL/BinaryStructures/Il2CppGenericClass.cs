#pragma warning disable 8618
//Disable null check because this stuff is initialized by reflection
namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppGenericClass
    {
        public long typeDefinitionIndex; /* the generic type definition */
        public Il2CppGenericContext context; /* a context that contains the type instantiation doesn't contain any method instantiation */
        public ulong cached_class; /* if present, the Il2CppClass corresponding to the instantiation.  */
    }
}