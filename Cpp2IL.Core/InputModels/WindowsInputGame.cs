using System;
using System.IO;
using System.Linq;
using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.InputModels
{
    public class WindowsInputGame : InputGame
    {
        public static WindowsInputGame? TryGet(string[] paths, string? inputExeName = null)
        {
            if (paths.Length > 1)
                return null;

            var path = paths[0];
            string exeName;
            if (inputExeName != null) exeName = inputExeName;
            else if (File.Exists(path))
            {
                exeName = Path.GetFileNameWithoutExtension(path);
                path = Path.GetDirectoryName(path) ?? throw new("Could not get the directory name.");
            }
            else if (Directory.Exists(path))
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
            var dataPath = Path.Combine(path, $"{exeName}_Data");
            var metadataPath = Path.Combine(dataPath, "il2cpp_data", "Metadata", "global-metadata.dat");
            if (File.Exists(gameAssemblyPath) && File.Exists(metadataPath))
                return new(gameAssemblyPath, metadataPath, exeName, dataPath);

            return null;
        }

        public string RootDir { get; }

        // TODO: This uses the old(?) Program.cs approach, move to GuiUtils approach if required
        private WindowsInputGame(string binaryPath, string metadataPath, string exeName, string dataPath, bool useResources = false) // TODO: Allow user to explicity specify useResources = true
        {
            MetadataBytes = File.ReadAllBytes(metadataPath);
            BinaryBytes = File.ReadAllBytes(binaryPath);
            RootDir = Path.GetDirectoryName(binaryPath)!;

            var playerExe = Path.Combine(RootDir, exeName + ".exe");
            if (!File.Exists(playerExe))
            {
                Logger.WarnNewline("Couldn't find the UnityPlayer exe, falling back to resources!");
                useResources = true;
            }

            if (!useResources)
            {
                UnityVersion = Cpp2IlApi.DetermineUnityVersion(playerExe, dataPath);
                Logger.VerboseNewline($"Unity version determined by Cpp2IlApi: {UnityVersion}");
                if (UnityVersion.HasValue && UnityVersion.Value.Major < 4)
                {
                    Logger.VerboseNewline("Detected a potentially invalid Unity version, falling back to resources!"); // TODO: Maybe we should make this WarnNewline?
                    useResources = true;
                }
            }

            if (useResources)
            {
                var pggm = Path.Combine(dataPath, "globalgamemanagers");
                var uggm = Path.Combine(dataPath, "data.unity3d");
                if (File.Exists(pggm))
                    UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(File.ReadAllBytes(pggm));
                else if (File.Exists(uggm))
                {
                    using var stream = File.OpenRead(uggm);
                    UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(stream);
                }
            }
        }

        public override byte[] MetadataBytes { get; }
        public override byte[] BinaryBytes { get; }
        public override UnityVersion? UnityVersion { get; }
    }
}