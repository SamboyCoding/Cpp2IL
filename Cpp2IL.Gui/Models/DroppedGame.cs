using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Extensions;
using LibCpp2IL;

namespace Cpp2IL.Gui.Models
{
    public abstract class DroppedGame
    {
        public static DroppedGame? ForPaths(string[] paths)
        {
            if (paths.Length > 2)
                paths = MiscExtensions.SubArray(paths, 0, 2);

            if (LooseFilesDroppedGame.TryGet(paths) is { } lfdg)
                return lfdg;

            if (DroppedWindowsGame.TryGet(paths) is { } dwg)
                return dwg;

            if (DroppedSingleApkGame.TryGet(paths) is { } dsag)
                return dsag;

            return null;
        }
        
        public abstract byte[] MetadataBytes { get; }
        public abstract byte[] BinaryBytes { get; }
        public abstract UnityVersion? UnityVersion { get; }
    }
}