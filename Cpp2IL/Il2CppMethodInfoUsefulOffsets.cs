using System;
using System.Collections.Generic;
using System.Linq;
using LibCpp2IL;

namespace Cpp2IL
{
    public static class Il2CppMethodInfoUsefulOffsets
    {
        public const int X86_KLASS_OFFSET = 0x00; //TODO
        public const int X86_64_KLASS_OFFSET = 0x18;
        
        public static readonly List<UsefulOffset> UsefulOffsets = new List<UsefulOffset>
        {
            //32-bit offsets:
            new UsefulOffset("klass", X86_KLASS_OFFSET, typeof(ushort), true),
            
            //64-bit offsets:
            new UsefulOffset("klass", X86_64_KLASS_OFFSET, typeof(IntPtr), false),
        };

        public static bool IsKlassPtr(uint offset)
        {
            return GetOffsetName(offset) == "klass";
        }

        public static string? GetOffsetName(uint offset)
        {
            var is32Bit = LibCpp2IlMain.ThePe!.is32Bit;

            return UsefulOffsets.FirstOrDefault(o => o.is32Bit == is32Bit && o.offset == offset)?.name;
        }
        
        public class UsefulOffset
        {
            public UsefulOffset(string name, uint offset, Type type, bool is32Bit)
            {
                this.name = name;
                this.offset = offset;
                this.type = type;
                this.is32Bit = is32Bit;
            }

            public string name;
            public uint offset;
            public Type type;
            public bool is32Bit;
        }
    }
}