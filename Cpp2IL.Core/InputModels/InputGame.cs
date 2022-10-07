using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Extensions;

namespace Cpp2IL.Core.InputModels
{
    public abstract class InputGame
    {
        public static InputGame? ForPaths(string[] paths)
        {
            if (paths.Length > 3)
                paths = MiscExtensions.SubArray(paths, 0, 3);

            if (LooseInputGame.TryGet(paths) is { } lig)
                return lig;

            if (WindowsInputGame.TryGet(paths) is { } wig)
                return wig;

            if (XapkInputGame.TryGet(paths) is { } xaig)
                return xaig;

            if (ApkInputGame.TryGet(paths) is { } aig)
                return aig;

            return null;
        }
        public static InputGame? ForPath(string path, string? inputExeName)
        {
            var paths = new string[] { path };
            if (paths.Length > 3)
                paths = MiscExtensions.SubArray(paths, 0, 3);

            if (WindowsInputGame.TryGet(paths, inputExeName) is { } wig)
                return wig;

            if (XapkInputGame.TryGet(paths) is { } xaig)
                return xaig;

            if (ApkInputGame.TryGet(paths) is { } aig)
                return aig;

            return null;
        }

        public abstract byte[] MetadataBytes { get; }
        public abstract byte[] BinaryBytes { get; }
        public abstract UnityVersion? UnityVersion { get; }
    }
}