using System;
using System.Collections.Generic;
using System.Linq;
using LibCpp2IL;

namespace Cpp2IL
{
    public static class Il2CppMethodDefinitionUsefulOffsets
    {
        public const int X86_SLOT_OFFSET = 0x00; //TODO
        public const int X86_64_SLOT_OFFSET = 0x48;
        
        public static readonly List<UsefulOffset> UsefulOffsets = new List<UsefulOffset>
        {
            //32-bit offsets:
            new UsefulOffset("slot", X86_SLOT_OFFSET, typeof(ushort), true),
            
            //64-bit offsets:
            new UsefulOffset("slot", X86_64_SLOT_OFFSET, typeof(ushort), false),
            new UsefulOffset("klass", 0x18, typeof(IntPtr), false),
            
            //TODO What *exactly* is this and when is it present? Found in Audica - TargetSpawnerLayoutUtil#UpdatePositions.
            new UsefulOffset("methodPtr", 0x30, typeof(IntPtr), false),
        };

        public static bool IsSlotOffset(uint offset) => GetOffsetName(offset) == "slot";
        public static bool IsKlassPtr(uint offset) => GetOffsetName(offset) == "klass";
        public static bool IsMethodPtr(uint offset) => GetOffsetName(offset) == "methodPtr";

        public static string? GetOffsetName(uint offset)
        {
            var is32Bit = LibCpp2IlMain.Binary!.is32Bit;

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