namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppGenericClass : ReadableClass
    {
        public long typeDefinitionIndex; /* the generic type definition */
        public Il2CppGenericContext context; /* a context that contains the type instantiation doesn't contain any method instantiation */
        public ulong cached_class; /* if present, the Il2CppClass corresponding to the instantiation.  */
        
        public override void Read(ClassReadingBinaryReader reader)
        {
            typeDefinitionIndex = reader.ReadNInt();
            context = reader.ReadReadableHereNoLock<Il2CppGenericContext>();
            cached_class = reader.ReadNUint();
        }
    }
}