namespace LibCpp2IL.Metadata
{
    public class Il2CppPropertyDefinition
    {
        public int nameIndex;
        public int get;
        public int set;
        public uint attrs;
        [Version(Max = 24)] public int customAttributeIndex;
        public uint token;
    }
}