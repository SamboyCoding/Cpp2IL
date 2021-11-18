namespace LibCpp2IL.Metadata
{
    public class Il2CppAssemblyDefinition
    {
        public int ImageIndex;
        [Version(Min = 24.1f)] public uint Token;
        [Version(Max = 24.0f)] public int CustomAttributeIndex;
        public int ReferencedAssemblyStart;
        public int ReferencedAssemblyCount;
        public Il2CppAssemblyNameDefinition AssemblyName;

        public Il2CppImageDefinition Image => LibCpp2IlMain.TheMetadata!.imageDefinitions[ImageIndex];

        public override string ToString() => AssemblyName.ToString();
    }
}