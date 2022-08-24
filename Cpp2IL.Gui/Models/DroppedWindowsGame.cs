using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.Gui.Models
{
    public class DroppedWindowsGame : DroppedGame
    {
        public override byte[] MetadataBytes { get; }
        public override byte[] BinaryBytes { get; }
        public override UnityVersion? UnityVersion { get; }
        
        public string RootDir { get; }

        private DroppedWindowsGame(string metadataPath, string binaryPath, string exeName)
        {
            MetadataBytes = File.ReadAllBytes(metadataPath);
            BinaryBytes = File.ReadAllBytes(binaryPath);
            RootDir = Path.GetDirectoryName(binaryPath)!;

            var playerExe = Path.Combine(RootDir, exeName + ".exe");
            if (!File.Exists(playerExe))
                throw new($"Somehow the player exe {playerExe} has disappeared");

            UnityVersion = GuiUtils.ReadFileVersionFromUnityExeXPlatform(playerExe);
        }

        public static DroppedWindowsGame? TryGet(string[] paths)
        {
            if (paths.Length > 1)
                return null;
            
            var path = paths[0];
            string exeName;
            if (File.Exists(path))
            {
                exeName = Path.GetFileNameWithoutExtension(path);
                path = Path.GetDirectoryName(path) ?? throw new("Could not get directory name");
            }
            else if(Directory.Exists(path))
            {
                var temp = Directory.GetFiles(path)
                    .Where(p => Path.GetExtension(p) == ".exe")
                    .FirstOrDefault(p => !MiscUtils.BlacklistedExecutableFilenames.Contains(Path.GetFileName(p)));

                if (temp == null)
                    return null;

                exeName = Path.GetFileNameWithoutExtension(temp);
            }
            else
            {
                throw new FileNotFoundException("Could not find the required file or directory.");
            }

            var gameAssemblyPath = Path.Combine(path, "GameAssembly.dll");
            var metadataPath = Path.Combine(path, $"{exeName}_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
            if (File.Exists(gameAssemblyPath) && File.Exists(metadataPath))
                return new(metadataPath, gameAssemblyPath, exeName);

            return null;
        }
    }
}