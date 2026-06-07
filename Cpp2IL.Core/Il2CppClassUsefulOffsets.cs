using System;
using System.Collections.Generic;
using System.Linq;

namespace Cpp2IL.Core;

public static class Il2CppClassUsefulOffsets
{
    public const int X86_INTERFACE_OFFSETS_OFFSET = 0x50;
    public const int X86_64_INTERFACE_OFFSETS_OFFSET = 0xB0;

    public static int GetVtableOffset(float metadataVersion, bool is32Bit) =>
        metadataVersion >= 24.2f
            ? is32Bit ? 0x999 /*TODO*/ : 0x138
            : is32Bit ? 0x999 /*TODO*/ : 0x128;

    public static readonly List<UsefulOffset> UsefulOffsets =
    [
        new("cctor_finished", 0x74, typeof(uint), true),
        new("flags1", 0xBB, typeof(byte), true),
        //new UsefulOffset("interface_offsets_count", 0x12A, typeof(ushort), true), //TODO
        // new UsefulOffset("rgctx_data", 0xC0, typeof(IntPtr), true), //TODO
        new("interfaceOffsets", X86_INTERFACE_OFFSETS_OFFSET, typeof(IntPtr), true),
        new("static_fields", 0x5C, typeof(IntPtr), true),
        //new UsefulOffset("vtable", 0x138, typeof(IntPtr), true), //TODO

        //64-bit offsets:
        new("elementType", 0x40, typeof(IntPtr), false),
        new("interfaceOffsets", X86_64_INTERFACE_OFFSETS_OFFSET, typeof(IntPtr), false),
        new("static_fields", 0xB8, typeof(IntPtr), false),
        new("rgctx_data", 0xC0, typeof(IntPtr), false),
        new("cctor_finished", 0xE0, typeof(uint), false),
        new("interface_offsets_count", 0x12A, typeof(ushort), false),
        new("flags1", 0x132, typeof(byte), false),
        new("flags2", 0x133, typeof(byte), false),
        new("vtable", 0x138, typeof(IntPtr), false)
    ];

    public static bool IsStaticFieldsPtr(uint offset, bool is32Bit)
    {
        return GetOffsetName(offset, is32Bit) == "static_fields";
    }

    public static bool IsInterfaceOffsetsPtr(uint offset, bool is32Bit)
    {
        return GetOffsetName(offset, is32Bit) == "interfaceOffsets";
    }

    public static bool IsInterfaceOffsetsCount(uint offset, bool is32Bit)
    {
        return GetOffsetName(offset, is32Bit) == "interface_offsets_count";
    }

    public static bool IsRGCTXDataPtr(uint offset, bool is32Bit)
    {
        return GetOffsetName(offset, is32Bit) == "rgctx_data";
    }

    public static bool IsElementTypePtr(uint offset, bool is32Bit)
    {
        return GetOffsetName(offset, is32Bit) == "elementType";
    }

    public static bool IsPointerIntoVtable(uint offset, float metadataVersion, bool is32Bit)
    {
        return offset >= GetVtableOffset(metadataVersion, is32Bit);
    }

    public static string? GetOffsetName(uint offset, bool is32Bit) =>
        UsefulOffsets.FirstOrDefault(o => o.is32Bit == is32Bit && o.offset == offset)?.name;

    public class UsefulOffset(string name, uint offset, Type type, bool is32Bit)
    {
        public string name = name;
        public uint offset = offset;
        public Type type = type;
        public bool is32Bit = is32Bit;
    }
}
