using System.IO;
using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.InputModels
{
    public class LooseInputGame : InputGame
    {
        private readonly string BinaryPath;
        private readonly string MetadataPath;

        public static LooseInputGame? TryGet(string[] paths)
        {
            if (paths.Length < 2)
                return null;
            string? metadataPath = null;
            string? binaryPath = null;
            UnityVersion? uv = null;

            foreach (var path in paths) // Using a loop over Linq to reduce cycles
            {
                if (!File.Exists(path))
                    return null;
                if (path.EndsWith(".dat"))
                {
                    var signature = new byte[4];
                    using (var fs = File.OpenRead(path))
                    {
                        fs.Read(signature, 0, 4);
                    }
                    if (Il2CppMetadata.HasMetadataHeader(signature)) metadataPath = path;
                    else return null;
                }
                else
                {
                    if (uv == null)
                    {
                        var uvi = MiscUtils.GetVersionFromFile(path, true);
                        if (uvi != null) uv = uvi;
                        else binaryPath = path;
                    }
                    else binaryPath = path;
                }
            }

            if (metadataPath == null || binaryPath == null)
                return null;

            return new(binaryPath, metadataPath, uv);
        }

        public LooseInputGame(string binaryPath, string metadataPath, UnityVersion? uv)
        {
            BinaryPath = binaryPath;
            MetadataPath = metadataPath;
            UnityVersion = uv;
        }

        public override byte[] MetadataBytes => File.ReadAllBytes(MetadataPath);
        public override byte[] BinaryBytes => File.ReadAllBytes(BinaryPath);
        public override UnityVersion? UnityVersion { get; }
    }
}