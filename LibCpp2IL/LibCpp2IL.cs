using System;
using System.IO;
using System.Linq;
using LibCpp2IL.Metadata;

namespace LibCpp2IL
{
    public static class LibCpp2IlMain
    {
        public static float MetadataVersion = 24f;
        public static PE.PE? ThePe;
        public static Il2CppMetadata? TheMetadata;

        public static bool LoadFromFile(string pePath, string metadataPath, int[] unityVersion)
        {
            TheMetadata = Il2CppMetadata.ReadFrom(metadataPath, unityVersion);

            if (TheMetadata == null)
                return false;
            
            var peBytes = File.ReadAllBytes(pePath);
            ThePe = new PE.PE(new MemoryStream(peBytes, 0, peBytes.Length, false, true), TheMetadata.maxMetadataUsages);

            return ThePe.PlusSearch(TheMetadata.methodDefs.Count(x => x.methodIndex >= 0), TheMetadata.typeDefs.Length);
        }
    }
}