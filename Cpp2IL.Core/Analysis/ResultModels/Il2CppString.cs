namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class Il2CppString
    {
        public string ContainedString;
        public ulong Address;

        public Il2CppString(string containedString, ulong addr)
        {
            ContainedString = containedString;
            Address = addr;
        }

        public override string ToString()
        {
            return $"{{il2cpp string, value = \"{ContainedString}\", address = 0x{Address:X}}}";
        }
    }
}