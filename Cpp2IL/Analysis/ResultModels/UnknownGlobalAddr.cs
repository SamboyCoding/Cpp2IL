namespace Cpp2IL.Analysis.ResultModels
{
    public class UnknownGlobalAddr
    {
        public ulong addr;

        public UnknownGlobalAddr(ulong a)
        {
            addr = a;
        }

        public override string ToString()
        {
            return $"{{Unknown Global at 0x{addr:X}}}";
        }
    }
}