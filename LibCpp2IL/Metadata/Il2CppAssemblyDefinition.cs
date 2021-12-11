using System;
using System.Linq;

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

        public Il2CppAssemblyDefinition[] ReferencedAssemblies => ReferencedAssemblyStart < 0
            ? Array.Empty<Il2CppAssemblyDefinition>()
            : LibCpp2IlMain.TheMetadata!.referencedAssemblies.SubArray(ReferencedAssemblyStart, ReferencedAssemblyCount)
                .Select(idx => LibCpp2IlMain.TheMetadata.AssemblyDefinitions[idx])
                .ToArray();

        public override string ToString() => AssemblyName.ToString();
    }
}