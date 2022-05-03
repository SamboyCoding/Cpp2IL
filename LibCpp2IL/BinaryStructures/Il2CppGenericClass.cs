using LibCpp2IL.Metadata;

namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppGenericClass : ReadableClass
    {
        public long TypeDefinitionIndex; /* the generic type definition */
        public Il2CppGenericContext Context = null!; /* a context that contains the type instantiation doesn't contain any method instantiation */
        public ulong CachedClass; /* if present, the Il2CppClass corresponding to the instantiation.  */
        
        public Il2CppTypeDefinition TypeDefinition => LibCpp2IlMain.TheMetadata!.typeDefs[(int)TypeDefinitionIndex];
        
        public override void Read(ClassReadingBinaryReader reader)
        {
            TypeDefinitionIndex = reader.ReadNInt();
            Context = reader.ReadReadableHereNoLock<Il2CppGenericContext>();
            CachedClass = reader.ReadNUint();
        }
    }
}