using System.Collections.Generic;

namespace Cpp2IL.Metadata
{
    public class Il2CppTypeDefinition
    {
        public int nameIndex;
        public int namespaceIndex;
        [Version(Max = 24)] public int customAttributeIndex;
        public int byvalTypeIndex;
        public int byrefTypeIndex;

        public int declaringTypeIndex;
        public int parentIndex;
        public int elementTypeIndex; // we can probably remove this one. Only used for enums

        [Version(Max = 24.1f)] public int rgctxStartIndex;
        [Version(Max = 24.1f)] public int rgctxCount;

        public int genericContainerIndex;

        public uint flags;

        public int firstFieldIdx;
        public int firstMethodId;
        public int firstEventId;
        public int firstPropertyId;
        public int nestedTypesStart;
        public int interfacesStart;
        public int vtableStart;
        public int interfaceOffsetsStart;

        public ushort method_count;
        public ushort propertyCount;
        public ushort field_count;
        public ushort eventCount;
        public ushort nested_type_count;
        public ushort vtable_count;
        public ushort interfaces_count;
        public ushort interface_offsets_count;

        // bitfield to portably encode boolean values as single bits
        // 01 - valuetype;
        // 02 - enumtype;
        // 03 - has_finalize;
        // 04 - has_cctor;
        // 05 - is_blittable;
        // 06 - is_import_or_windows_runtime;
        // 07-10 - One of nine possible PackingSize values (0, 1, 2, 4, 8, 16, 32, 64, or 128)
        public uint bitfield;
        public uint token;

        public Il2CppInterfaceOffset[] InterfaceOffsets => Program.Metadata.interfaceOffsets.SubArray(interfaceOffsetsStart, interface_offsets_count);
    }
}