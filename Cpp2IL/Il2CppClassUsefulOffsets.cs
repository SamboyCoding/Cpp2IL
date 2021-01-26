using System;
using System.Collections.Generic;
using System.Linq;
using LibCpp2IL;

namespace Cpp2IL
{
    public static class Il2CppClassUsefulOffsets
    {
        public const int X86_INTERFACE_OFFSET_COUNT_OFFSET = 0x50;
        public const int X86_64_INTERFACE_OFFSET_COUNT_OFFSET = 0xB0;
        
        public static readonly List<UsefulOffset> UsefulOffsets = new List<UsefulOffset>
        {
            //32-bit offsets:
            new UsefulOffset("cctor_finished", 0x74, typeof(uint), true),
            new UsefulOffset("flags1", 0xBB, typeof(byte), true),
            new UsefulOffset("interfaceOffsets", X86_INTERFACE_OFFSET_COUNT_OFFSET, typeof(IntPtr), true),
            new UsefulOffset("static_fields", 0x5C, typeof(IntPtr), true),
            
            //64-bit offsets:
            new UsefulOffset("interfaceOffsets", X86_64_INTERFACE_OFFSET_COUNT_OFFSET, typeof(IntPtr), false)
        };

        public static bool IsStaticFieldsPtr(uint offset)
        {
            return GetOffsetName(offset) == "static_fields";
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