namespace LibCpp2IL.Metadata
{
    public class Il2CppFieldDefinition
    {
        public int nameIndex;
        public int typeIndex;
        [Version(Max = 24)] public int customAttributeIndex;
        public uint token;
    }
}