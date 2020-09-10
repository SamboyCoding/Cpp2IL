using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata
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
        public int firstMethodIdx;
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

        public Il2CppInterfaceOffset[] InterfaceOffsets
        {
            get
            {
                if (interfaceOffsetsStart < 0) return new Il2CppInterfaceOffset[0];

                return LibCpp2IlMain.TheMetadata!.interfaceOffsets.SubArray(interfaceOffsetsStart, interface_offsets_count);
            }
        }

        public int TypeIndex => LibCpp2IlReflection.GetTypeIndexFromType(this);

        public Il2CppAssemblyDefinition? DeclaringAssembly
        {
            get
            {
                if (LibCpp2IlMain.TheMetadata == null) return null;
                var typeIdx = TypeIndex;

                foreach (var assemblyDefinition in LibCpp2IlMain.TheMetadata.assemblyDefinitions)
                {
                    var lastIdx = assemblyDefinition.firstTypeIndex + assemblyDefinition.typeCount - 1;
                    if (assemblyDefinition.firstTypeIndex <= typeIdx && typeIdx <= lastIdx)
                        return assemblyDefinition;
                }

                return null;
            }
        }

        public string? Namespace => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(namespaceIndex);

        public string? Name => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(nameIndex);

        public string? FullName => LibCpp2IlMain.TheMetadata == null || Namespace == null ? 
            null : 
            (string.IsNullOrEmpty(Namespace) ? "" : Namespace + ".") + Name;

        public Il2CppTypeDefinition? BaseType => LibCpp2IlReflection.GetTypeDefinitionByTypeIndex(parentIndex);

        public Il2CppFieldDefinition[]? Fields => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.fieldDefs.Skip(firstFieldIdx).Take(field_count).ToArray();

        public FieldAttributes[]? FieldAttributes => Fields?
            .Select(f => f.typeIndex)
            .Select(idx => LibCpp2IlMain.ThePe!.types[idx])
            .Select(t => (FieldAttributes) t.attrs)
            .ToArray();

        public object?[]? FieldDefaults => Fields?
            .Select((f, idx) => (f.FieldIndex, FieldAttributes![idx]))
            .Select(tuple => (tuple.Item2 & System.Reflection.FieldAttributes.HasDefault) != 0 ? LibCpp2IlMain.TheMetadata!.GetFieldDefaultValueFromIndex(tuple.FieldIndex) : null)
            .Select(def => def == null ? null : LibCpp2ILUtils.GetDefaultValue(def.dataIndex, def.typeIndex, LibCpp2IlMain.TheMetadata!, LibCpp2IlMain.ThePe!))
            .ToArray();

        public Il2CppFieldReflectionData[]? FieldInfos
        {
            get
            {
                var fields = Fields;
                var attributes = FieldAttributes;
                var defaults = FieldDefaults;

                return fields?
                    .Select((t, i) => new Il2CppFieldReflectionData {attributes = attributes![i], field = t, defaultValue = defaults![i]})
                    .ToArray();
            }
        }

        public Il2CppMethodDefinition[]? Methods => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.methodDefs.Skip(firstMethodIdx).Take(method_count).ToArray();

        public Il2CppPropertyDefinition[]? Properties => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.propertyDefs.Skip(firstPropertyId).Take(propertyCount).ToArray();

        public Il2CppEventDefinition[]? Events => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.eventDefs.Skip(firstEventId).Take(eventCount).ToArray();

        public Il2CppTypeDefinition[]? NestedTypes => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.nestedTypeIndices.Skip(nestedTypesStart).Take(nested_type_count).Select(idx => LibCpp2IlMain.TheMetadata.typeDefs[idx]).ToArray();

        public Il2CppTypeReflectionData[]? Interfaces => LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.ThePe == null
            ? null
            : LibCpp2IlMain.TheMetadata.interfaceIndices
                .Skip(interfacesStart)
                .Take(interfaces_count)
                .Select(idx => LibCpp2IlMain.ThePe.types[idx])
                .Select(type => LibCpp2ILUtils.GetTypeReflectionData(type)!)
                .ToArray();

        public Il2CppTypeDefinition? DeclaringType => LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.ThePe == null || declaringTypeIndex < 0 ? null : LibCpp2IlMain.TheMetadata.typeDefs[LibCpp2IlMain.ThePe.types[declaringTypeIndex].data.classIndex];

        public override string ToString()
        {
            if (LibCpp2IlMain.TheMetadata == null) return base.ToString();

            return $"Il2CppTypeDefinition[namespace='{Namespace}', name='{Name}', parentType={BaseType?.ToString() ?? "null"}]";
        }
    }
}