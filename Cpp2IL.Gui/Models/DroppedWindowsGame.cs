using LibCpp2IL;

namespace Cpp2IL.Gui.Models
{
    public class DroppedWindowsGame : DroppedGame
    {
        public override byte[] MetadataBytes { get; }
        public override byte[] BinaryBytes { get; }
        public override UnityVersion? UnityVersion { get; }
    }
}