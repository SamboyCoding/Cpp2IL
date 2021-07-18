using System.Linq;
using LibCpp2IL;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class UnknownGlobalAddr
    {
        public ulong addr;

        public UnknownGlobalAddr(ulong a)
        {
            addr = a;
        }

        internal int RawAddr => (int) LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(addr);

        internal byte[] FirstTenBytes => LibCpp2IlMain.Binary!.GetRawBinaryContent().SubArray(RawAddr, 10);

        public override string ToString()
        {
            return $"{{Unknown Global at 0x{addr:X}, first ten bytes are [{string.Join(" ", FirstTenBytes)}], or as chars \"{string.Join("", FirstTenBytes.Select(b => (char) b))}\"}}";
        }
    }
}