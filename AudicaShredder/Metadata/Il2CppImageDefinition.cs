namespace AudicaShredder.Metadata
{
    public class Il2CppImageDefinition
    {
        public int nameIndex;
        public int assemblyIndex;

        public int typeStart;
        public uint typeCount;

        public int exportedTypeStart;
        public uint exportedTypeCount;

        public int entryPointIndex;
        public uint token;

        [Version(Min = 24.1f)] public int customAttributeStart;
        [Version(Min = 24.1f)] public uint customAttributeCount;
    }
}