using System;
using LibCpp2IL;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class Il2CppString
    {
        public string ContainedString;
        public ulong Address;
        public bool HasBeenUsedAsAString;

        public Il2CppString(string containedString, ulong addr)
        {
            ContainedString = containedString;

            if (!LibCpp2IlMain.Binary!.TryMapVirtualAddressToRaw(addr, out _))
                throw new Exception($"Invalid il2cpp string creation - 0x{addr:X} cannot be mapped to the binary.");
            
            Address = addr;
        }

        public override string ToString()
        {
            return $"{{il2cpp string, value = \"{ContainedString}\", address = 0x{Address:X}}}";
        }
    }
}