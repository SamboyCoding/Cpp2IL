using System.Linq;

namespace LibCpp2IL.Metadata
{
    public class Il2CppAssemblyDefinition
    {
        public int nameIndex;
        public int assemblyIndex;

        public int firstTypeIndex;
        public uint typeCount;

        public int exportedTypeStart;
        public uint exportedTypeCount;

        public int entryPointIndex;
        public uint token;

        [Version(Min = 24.1f)] public int customAttributeStart;
        [Version(Min = 24.1f)] public uint customAttributeCount;

        public string? Name => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(nameIndex);

        public Il2CppTypeDefinition[]? Types => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.typeDefs.Skip(firstTypeIndex).Take((int) typeCount).ToArray();
    }
}