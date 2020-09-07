namespace LibCpp2IL.Metadata
{
    public class Il2CppEventDefinition
    {
        public int nameIndex;
        public int typeIndex;
        public int add;
        public int remove;
        public int raise;
        [Version(Max = 24)] public int customAttributeIndex; //Not in 24.1 or 24.2
        public uint token;
    }
}