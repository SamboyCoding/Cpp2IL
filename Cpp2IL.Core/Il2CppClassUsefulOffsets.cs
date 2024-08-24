using System;
using System.Collections.Generic;
using System.Linq;
using LibCpp2IL;

namespace Cpp2IL.Core;

public static class Il2CppClassUsefulOffsets
{
    public const int X86_INTERFACE_OFFSETS_OFFSET = 0x50;
    public const int X86_64_INTERFACE_OFFSETS_OFFSET = 0xB0;

    private static readonly int V24_2_VTABLE_OFFSET = LibCpp2IlMain.Binary!.is32Bit ? 0x999 /*TODO*/ : 0x138;
    private static readonly int PRE_24_2_VTABLE_OFFSET = LibCpp2IlMain.Binary.is32Bit ? 0x999 /*TODO*/ : 0x128;

    public static readonly int VTABLE_OFFSET = LibCpp2IlMain.MetadataVersion >= 24.2 ? V24_2_VTABLE_OFFSET : PRE_24_2_VTABLE_OFFSET;

    public static readonly List<UsefulOffset> UsefulOffsets =
    [
        new UsefulOffset("cctor_finished", 0x74, typeof(uint), true),
        new UsefulOffset("flags1", 0xBB, typeof(byte), true),
        //new UsefulOffset("interface_offsets_count", 0x12A, typeof(ushort), true), //TODO
        // new UsefulOffset("rgctx_data", 0xC0, typeof(IntPtr), true), //TODO
        new UsefulOffset("interfaceOffsets", X86_INTERFACE_OFFSETS_OFFSET, typeof(IntPtr), true),
        new UsefulOffset("static_fields", 0x5C, typeof(IntPtr), true),
        //new UsefulOffset("vtable", 0x138, typeof(IntPtr), true), //TODO

        //64-bit offsets:
        new UsefulOffset("elementType", 0x40, typeof(IntPtr), false),
        new UsefulOffset("interfaceOffsets", X86_64_INTERFACE_OFFSETS_OFFSET, typeof(IntPtr), false),
        new UsefulOffset("static_fields", 0xB8, typeof(IntPtr), false),
        new UsefulOffset("rgctx_data", 0xC0, typeof(IntPtr), false),
        new UsefulOffset("cctor_finished", 0xE0, typeof(uint), false),
        new UsefulOffset("interface_offsets_count", 0x12A, typeof(ushort), false),
        new UsefulOffset("flags1", 0x132, typeof(byte), false),
        new UsefulOffset("flags2", 0x133, typeof(byte), false),
        new UsefulOffset("vtable", 0x138, typeof(IntPtr), false)
    ];

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

    public static bool IsElementTypePtr(uint offset)
    {
        return GetOffsetName(offset) == "elementType";
    }

    public static bool IsPointerIntoVtable(uint offset)
    {
        return offset >= VTABLE_OFFSET;
    }

    public static string? GetOffsetName(uint offset)
    {
        var is32Bit = LibCpp2IlMain.Binary!.is32Bit;

        return UsefulOffsets.FirstOrDefault(o => o.is32Bit == is32Bit && o.offset == offset)?.name;
    }

    public class UsefulOffset(string name, uint offset, Type type, bool is32Bit)
    {
        public string name = name;
        public uint offset = offset;
        public Type type = type;
        public bool is32Bit = is32Bit;
    }
}
