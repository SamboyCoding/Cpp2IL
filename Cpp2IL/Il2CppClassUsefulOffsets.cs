using System;
using System.Collections.Generic;
using System.Linq;
using LibCpp2IL;

namespace Cpp2IL
{
    public static class Il2CppClassUsefulOffsets
    {
        public const int X86_INTERFACE_OFFSETS_OFFSET = 0x50;
        public const int X86_64_INTERFACE_OFFSETS_OFFSET = 0xB0;

        public const int X86_VTABLE_OFFSET = 0x999; //todo
        public const int X86_64_VTABLE_OFFSET = 0x138; //TODO Check if this is lower (0x128?) on older metadata versions

        public static readonly int VTABLE_OFFSET = LibCpp2IlMain.ThePe!.is32Bit ? X86_VTABLE_OFFSET : X86_64_VTABLE_OFFSET;

        public static readonly List<UsefulOffset> UsefulOffsets = new List<UsefulOffset>
        {
            //32-bit offsets:
            new UsefulOffset("cctor_finished", 0x74, typeof(uint), true),
            new UsefulOffset("flags1", 0xBB, typeof(byte), true),
            //new UsefulOffset("interface_offsets_count", 0x12A, typeof(ushort), true), //TODO
            // new UsefulOffset("rgctx_data", 0xC0, typeof(IntPtr), true), //TODO
            new UsefulOffset("interfaceOffsets", X86_INTERFACE_OFFSETS_OFFSET, typeof(IntPtr), true),
            new UsefulOffset("static_fields", 0x5C, typeof(IntPtr), true),
            //new UsefulOffset("vtable", 0x138, typeof(IntPtr), true), //TODO

            //64-bit offsets:
            new UsefulOffset("interface_offsets_count", 0x12A, typeof(ushort), false),
            new UsefulOffset("interfaceOffsets", X86_64_INTERFACE_OFFSETS_OFFSET, typeof(IntPtr), false),
            new UsefulOffset("static_fields", 0xB8, typeof(IntPtr), false),
            new UsefulOffset("rgctx_data", 0xC0, typeof(IntPtr), false),
            new UsefulOffset("cctor_finished", 0xE0, typeof(uint), false),
            new UsefulOffset("flags1", 0x132, typeof(byte), false),
            new UsefulOffset("flags2", 0x133, typeof(byte), false),
            new UsefulOffset("vtable", 0x138, typeof(IntPtr), false),
        };

        public static bool IsStaticFieldsPtr(uint offset)
        {
            return GetOffsetName(offset) == "static_fields";
        }

        public static bool IsInterfaceOffsetsPtr(uint offset)
        {
            return GetOffsetName(offset) == "interfaceOffsets";
        }
        
        public static bool IsInterfaceOffsetsCount(uint offset)
        {
            return GetOffsetName(offset) == "interface_offsets_count";
        }

        public static bool IsRGCTXDataPtr(uint offset)
        {
            return GetOffsetName(offset) == "rgctx_data";
        }

        public static bool IsPointerIntoVtable(uint offset)
        {
            return offset >= VTABLE_OFFSET;
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