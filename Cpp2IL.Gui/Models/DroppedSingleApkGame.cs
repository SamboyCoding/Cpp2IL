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
using Cpp2IL.Core.Logging;

namespace Cpp2IL.Gui.Models
{
    public class DroppedSingleApkGame : DroppedGame
    {
        private static readonly string[] Traverse = new[] { "x86_64", "x86", "arm64-v8a", "armeabi-v7a" }; // TODO: Review this list and finish if required

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
            foreach (var e in zip.Entries)
            {
                if (e == null)
                    continue;
                if (e.Name == "global-metadata.dat")
                {
                    if (md != null)
                    {
                        Logger.WarnNewline("Found duplicate global-metadata.dat, skipping."); // TODO: Add an interactive GUI picker
                        continue;
                    }
                    md = e.ReadBytes();
                }
                else if (e.Name == "libil2cpp.so")
                {
                    libs.Add(e.FullName.Split("/")[^2], e);
                }
            }
            
            if (md == null)
                return null;
            foreach (var spec in Traverse)
            {
                if (libs.ContainsKey(spec))
                    return new(md, libs[spec].ReadBytes());
            }
            return libs.Count > 0 ? new(md, libs.First().Value.ReadBytes()) : null;
        }
    }
}
