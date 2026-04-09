using System;
using System.Collections.Generic;
using System.Linq;
using LibCpp2IL;

namespace Cpp2IL.Core;

public static class Il2CppClassUsefulOffsets
{
    public const int X86_INTERFACE_OFFSETS_OFFSET = 0x50;
    public const int X86_64_INTERFACE_OFFSETS_OFFSET = 0xB0;

    public static int GetVtableOffset(Il2CppBinary binary, float metadataVersion)
    {
        var v24_2_vtableOffset = binary.is32Bit ? 0x999 /*TODO*/ : 0x138;
        var pre24_2_vtableOffset = binary.is32Bit ? 0x999 /*TODO*/ : 0x128;

        return metadataVersion >= 24.2f ? v24_2_vtableOffset : pre24_2_vtableOffset;
    }

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

    public static bool IsStaticFieldsPtr(uint offset, Il2CppBinary binary)
    {
        return GetOffsetName(offset, binary) == "static_fields";
    }

    public static bool IsInterfaceOffsetsPtr(uint offset, Il2CppBinary binary)
    {
        return GetOffsetName(offset, binary) == "interfaceOffsets";
    }

    public static bool IsInterfaceOffsetsCount(uint offset, Il2CppBinary binary)
    {
        return GetOffsetName(offset, binary) == "interface_offsets_count";
    }

    public static bool IsRGCTXDataPtr(uint offset, Il2CppBinary binary)
    {
        return GetOffsetName(offset, binary) == "rgctx_data";
    }

    public static bool IsElementTypePtr(uint offset, Il2CppBinary binary)
    {
        return GetOffsetName(offset, binary) == "elementType";
    }

    public static bool IsPointerIntoVtable(uint offset, Il2CppBinary binary, float metadataVersion)
    {
        return offset >= GetVtableOffset(binary, metadataVersion);
    }

    public static string? GetOffsetName(uint offset, Il2CppBinary binary)
    {
        var is32Bit = binary.is32Bit;

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
