using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.InputModels
{
    public class XapkInputGame : InputGame
    {
        private static readonly string[] Traverse = new[] { "x86_64", "x86", "arm64-v8a", "arm64_v8a", "armeabi-v7a", "armeabi_v7a" };

        public static XapkInputGame? TryGet(string[] paths)
        {
            UnityVersion? uv = null;
            var xapk = paths.FirstOrDefault(x => x.EndsWith(".xapk") || x.EndsWith(".apkm") || x.EndsWith(".apks"));
            if (xapk == null)
                return null;

            using var zip = ZipFile.OpenRead(xapk);

            ZipArchive? mainApk = null;
            Dictionary<string, ZipArchive> configs = new();

            foreach (var apk in zip.Entries)
            {
                if (apk == null || !apk.Name.EndsWith(".apk")) continue;
                var spl = apk.Name.Split('.');
                if (spl[0].Contains("config"))
                {
                    configs.Add(spl[1], new ZipArchive(apk.Open()));
                }
                else
                {
                    mainApk = new ZipArchive(apk.Open());
                }
            }

            if (mainApk == null || configs.Count == 0)
                return null;

            try
            {
                byte[]? md = null;
                Dictionary<string, ZipArchiveEntry> libs = new();

                if ((md = mainApk.Entries.FirstOrDefault(x => x.Name == "global-metadata.dat")?.ReadBytes()) == null)
                    return null;

                var zggm = mainApk.Entries.FirstOrDefault(x => x.Name == "globalgamemanagers");
                var zdu3d = mainApk.Entries.FirstOrDefault(x => x.Name == "data.unity3d");

                foreach (var kv in configs)
                {
                    var ze = kv.Value.Entries.FirstOrDefault(x => x.Name == "libil2cpp.so");
                    if (ze != null)
                        libs.Add(kv.Key, ze);
                }

                if (zggm != null)
                {
                    Logger.InfoNewline("Reading globalgamemanagers to determine Unity version...", "XAPK");
                    var ggmBytes = new byte[0x40];
                    using var ggmStream = zggm.Open();

                    // ReSharper disable once MustUseReturnValue
                    ggmStream.Read(ggmBytes, 0, 0x40);

                    uv = Cpp2IlApi.GetVersionFromGlobalGameManagers(ggmBytes);
                }
                else if (zdu3d != null)
                {
                    Logger.InfoNewline("Reading data.unity3d to determine Unity version...", "XAPK");
                    using var du3dStream = zdu3d!.Open();

                    uv = Cpp2IlApi.GetVersionFromDataUnity3D(du3dStream);
                }

                if (uv == null && paths.Length > 1)
                {
                    Logger.InfoNewline("Couldn't determine the game version from internal resources, trying dropped files.", "XAPK");
                    foreach (var path in paths)
                    {
                        if (path != xapk && (uv = MiscUtils.GetVersionFromFile(path, false, "XAPK")) != null) break;
                    }
                }

                foreach (var spec in Traverse)
                {
                    if (libs.ContainsKey(spec))
                        return new(libs[spec].ReadBytes(), md, uv);
                }

                return null;
            }
#if DEBUG
            catch
            {
                throw;
            }
#endif
            finally
            {
                foreach (var za in configs.Values)
                    za.Dispose();
            }
        }

        private XapkInputGame(byte[] binary, byte[] metadata, UnityVersion? uv)
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