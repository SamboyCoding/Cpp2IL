using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using System.IO.Compression;
using Cpp2IL.Core.Extensions;
using System.Collections.Generic;

namespace Cpp2IL.Gui.Models
{
    public class DroppedSingleApkGame : DroppedGame
    {
        private static readonly string[] Traverse = new[]{"x86_64", "x86", "arm64-v8a", "armeabi-v7a"}; // TODO Review and finish if required

        public override byte[] MetadataBytes { get; }
        public override byte[] BinaryBytes { get; }
        public override UnityVersion? UnityVersion => null;

        private DroppedSingleApkGame(byte[] metadata, byte[] binary)
        {
            MetadataBytes = metadata;
            BinaryBytes = binary;
        }

        public static DroppedSingleApkGame? TryGet(string[] paths)
        {
            if (paths.Length > 1)
                return null;
            var path = paths[0];
            if (!File.Exists(path))
                throw new FileNotFoundException("Could not find the required file.");
            using var zip = ZipFile.OpenRead(path);
            Dictionary<string, ZipArchiveEntry> libs = new();
            byte[]? md = null;
            foreach (var e in zip.Entries) {
                if (e == null) continue;
                if (e.Name == "global-metadata.dat") {
                    if (md != null) {
                        Console.WriteLine("[WARN] Found duplicate global-metadata.dat, skipping."); // TODO Replace with a proper logger/remove + TODO Add an interactive picker
                        continue;
                    }
                    md = e.ReadBytes();
                }
                else if (e.Name == "libil2cpp.so") {
                    libs.Add(e.FullName.Split("/")[^2], e);
                }
            }
            if (md == null) return null; // throw new("Could not find the global metadata, the game is obfuscated or the file is provided separately(obb/data?).");
            foreach (var spec in Traverse) {
                if (libs.ContainsKey(spec)) {
                    Console.WriteLine("Traverse found "+spec+" to be most fitting."); // Debug log, can be removed if unneeded
                    return new(md, libs[spec].ReadBytes());
                }
            }
            return libs.Count > 0 ? new(md, libs.First().Value.ReadBytes()) : null; // throw new("Could not find libil2cpp.so of any architecture, the file is provided separately(obb/data?).");
        }
    }
}