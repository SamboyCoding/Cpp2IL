using System.IO;
using System.Linq;
using AssetRipper.Primitives;
using LibCpp2IL;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Gui.Models
{
    public class LooseFilesDroppedGame : DroppedGame
    {
        private string MetadataPath;
        private string BinaryPath;
        
        public static LooseFilesDroppedGame? TryGet(string[] paths)
        {
            if (paths.Length != 2)
                //More than 2 files dropped
                return null;
            
            if (!File.Exists(paths[0]) || !File.Exists(paths[1]))
                //One of the files is missing
                return null;
            
            //if one of the files is a global-metadata and the other one is not a directory, we can assume the dropped files constitute a loose binary + metadata
            var potentialMetadata = paths.FirstOrDefault(p => p.ToLowerInvariant().EndsWith(".dat"));
            if (potentialMetadata == null)
                //No .dat files so we assume no metadata
                return null;
            
            var firstFourBytes = new byte[4];
            using (var fs = File.OpenRead(potentialMetadata))
            {
                fs.Read(firstFourBytes, 0, 4);
            }

            if (!Il2CppMetadata.HasMetadataHeader(firstFourBytes))
                //Not a metadata file
                return null;
            
            var potentialBinary = paths.FirstOrDefault(p => !p.ToLowerInvariant().EndsWith(".dat"));
            if (potentialBinary == null)
                return null;

            if (Directory.Exists(potentialBinary))
                //Other path is a directory
                return null;
            
            return new(potentialMetadata, potentialBinary);
        }

        public LooseFilesDroppedGame(string metadataPath, string binaryPath)
        {
            MetadataPath = metadataPath;
            BinaryPath = binaryPath;
        }

        public override byte[] MetadataBytes => File.ReadAllBytes(MetadataPath);
        public override byte[] BinaryBytes => File.ReadAllBytes(BinaryPath);
        public override UnityVersion? UnityVersion => null;
    }
}