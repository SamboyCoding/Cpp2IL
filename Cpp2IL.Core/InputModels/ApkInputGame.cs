using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.InputModels
{
    public class ApkInputGame : InputGame
    {
        private static readonly string[] Traverse = new[] { "x86_64", "x86", "arm64-v8a", "arm64_v8a", "armeabi-v7a", "armeabi_v7a" };

        public static ApkInputGame? TryGet(string[] paths)
        {
            UnityVersion? uv = null;
            var apk = paths.FirstOrDefault(x => x.EndsWith(".apk"));
            if (apk == null)
                return null;

            byte[]? md = null;
            ZipArchiveEntry? zggm = null;
            ZipArchiveEntry? zdu3d = null;
            Dictionary<string, ZipArchiveEntry> libs = new();
            using var zip = ZipFile.OpenRead(apk);

            foreach (var e in zip.Entries)
            {
                if (e == null)
                    continue;

                switch (e.Name)
                {
                    case "global-metadata.dat":
                        md = e.ReadBytes();
                        break;
                    case "libil2cpp.so":
                        libs.Add(e.FullName.Split('/')[^2], e);
                        break;
                    case "globalgamemanagers":
                        zggm = e;
                        break;
                    case "data.unity3d":
                        zdu3d = e;
                        break;
                }
            }

            if (md == null)
                return null;

            if (zggm != null)
            {
                Logger.InfoNewline("Reading globalgamemanagers to determine Unity version...", "APK");
                var ggmBytes = new byte[0x40];
                using var ggmStream = zggm.Open();

                // ReSharper disable once MustUseReturnValue
                ggmStream.Read(ggmBytes, 0, 0x40);

                uv = Cpp2IlApi.GetVersionFromGlobalGameManagers(ggmBytes);
            }
            else if (zdu3d != null)
            {
                Logger.InfoNewline("Reading data.unity3d to determine Unity version...", "APK");
                using var du3dStream = zdu3d!.Open();

                uv = Cpp2IlApi.GetVersionFromDataUnity3D(du3dStream);
            }

            if (uv == null && paths.Length > 1)
            {
                Logger.InfoNewline("Couldn't determine the game version from internal resources, trying dropped files.", "APK");
                foreach (var path in paths)
                {
                    if (path != apk && (uv = MiscUtils.GetVersionFromFile(path, false, "APK")) != null) break;
                }
            }

            foreach (var spec in Traverse)
            {
                if (libs.ContainsKey(spec))
                    return new(libs[spec].ReadBytes(), md, uv);
            }

            return null;
        }

        private ApkInputGame(byte[] binary, byte[] metadata, UnityVersion? uv)
        {
            MetadataBytes = metadata;
            BinaryBytes = binary;
            UnityVersion = uv;
        }

        public override byte[] MetadataBytes { get; }
        public override byte[] BinaryBytes { get; }
        public override UnityVersion? UnityVersion { get; }
    }
}