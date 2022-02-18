using System;
using System.Linq;
using System.Reflection;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata
{
    public class Il2CppTypeDefinition
    {
        public int nameIndex;
        public int namespaceIndex;
        [Version(Max = 24)] public int customAttributeIndex;
        public int byvalTypeIndex;

        [Version(Max = 24.5f)] //Removed in v27 
        public int byrefTypeIndex;

        public int declaringTypeIndex;
        public int parentIndex;
        public int elementTypeIndex; // we can probably remove this one. Only used for enums

        [Version(Max = 24.15f)] public int rgctxStartIndex;
        [Version(Max = 24.15f)] public int rgctxCount;

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
        // 11 - PackingSize is default
        // 12 - ClassSize is default
        // 13-16 - One of nine possible PackingSize values (0, 1, 2, 4, 8, 16, 32, 64, or 128) - the specified packing size (even for explicit layouts)
        public uint bitfield;
        public uint token;

        public bool IsValueType => (bitfield >> 0 & 0x1) == 1;
        public bool IsEnumType => (bitfield >> 1 & 0x1) == 1;
        public bool HasFinalizer => (bitfield >> 2 & 0x1) == 1;
        public bool HasCctor => (bitfield >> 3 & 0x1) == 1;
        public bool IsBlittable => (bitfield >> 4 & 0x1) == 1;
        public bool IsImportOrWindowsRuntime => (bitfield >> 5 & 0x1) == 1;
        public uint PackingSize => ((Il2CppPackingSizeEnum)(bitfield >> 6 & 0xF)).NumericalValue();
        public bool PackingSizeIsDefault => (bitfield >> 10 & 0x1) == 1;
        public bool ClassSizeIsDefault => (bitfield >> 11 & 0x1) == 1;
        public uint SpecifiedPackingSize => ((Il2CppPackingSizeEnum)(bitfield >> 12 & 0xF)).NumericalValue();
        public bool IsByRefLike => (bitfield >> 16 & 0x1) == 1;

        public Il2CppTypeDefinitionSizes RawSizes
        {
            get
            {
                var sizePtr = LibCpp2IlMain.Binary!.TypeDefinitionSizePointers[TypeIndex];
                return LibCpp2IlMain.Binary.ReadClassAtVirtualAddress<Il2CppTypeDefinitionSizes>(sizePtr);
            }
        }

        public int Size => RawSizes.native_size;

        public Il2CppInterfaceOffset[] InterfaceOffsets
        {
            get
            {
                if (interfaceOffsetsStart < 0) return new Il2CppInterfaceOffset[0];

                return LibCpp2IlMain.TheMetadata!.interfaceOffsets.SubArray(interfaceOffsetsStart, interface_offsets_count);
            }
        }

        public MetadataUsage?[] VTable
        {
            get
            {
                if (vtableStart < 0) return new MetadataUsage[0];

                return LibCpp2IlMain.TheMetadata!.VTableMethodIndices.SubArray(vtableStart, vtable_count).Select(v => MetadataUsage.DecodeMetadataUsage(v, 0)).ToArray();
            }
        }

        public int TypeIndex => LibCpp2IlReflection.GetTypeIndexFromType(this);

        public bool IsAbstract => ((TypeAttributes)flags & TypeAttributes.Abstract) != 0;

        private Il2CppImageDefinition? _cachedDeclaringAssembly;

        public Il2CppImageDefinition? DeclaringAssembly
        {
            get
            {
                if (_cachedDeclaringAssembly == null)
                {
                    if (LibCpp2IlMain.TheMetadata == null) return null;

                    LibCpp2ILUtils.PopulateDeclaringAssemblyCache();
                }

                return _cachedDeclaringAssembly;
            }
            internal set => _cachedDeclaringAssembly = value;
        }

        public Il2CppCodeGenModule? CodeGenModule => LibCpp2IlMain.Binary == null ? null : LibCpp2IlMain.Binary.GetCodegenModuleByName(DeclaringAssembly!.Name!);

        public Il2CppRGCTXDefinition[] RGCTXs
        {
            get
            {
                if (LibCpp2IlMain.MetadataVersion < 24.2f)
                {
                    //No codegen modules here.
                    return LibCpp2IlMain.TheMetadata!.RgctxDefinitions.Skip(rgctxStartIndex).Take(rgctxCount).ToArray();
                }

                var cgm = CodeGenModule;

                if (cgm == null)
                    return new Il2CppRGCTXDefinition[0];

                var rangePair = cgm.RGCTXRanges.FirstOrDefault(r => r.token == token);

                if (rangePair == null)
                    return new Il2CppRGCTXDefinition[0];

                return LibCpp2IlMain.Binary!.GetRGCTXDataForPair(cgm, rangePair);
            }
        }

        public ulong[] RGCTXMethodPointers
        {
            get
            {
                var index = LibCpp2IlMain.Binary!.GetCodegenModuleIndexByName(DeclaringAssembly!.Name!);

                if (index < 0)
                    return new ulong[0];

                var pointers = LibCpp2IlMain.Binary!.GetCodegenModuleMethodPointers(index);

                return RGCTXs
                    .Where(r => r.type == Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_METHOD)
                    .Select(r => pointers[r.MethodIndex])
                    .ToArray();
            }
        }

        private string? _cachedNamespace;

        public string? Namespace
        {
            get
            {
                if (_cachedNamespace == null)
                    _cachedNamespace = LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(namespaceIndex);

                return _cachedNamespace;
            }
        }

        private string? _cachedName;

        public string? Name
        {
            get
            {
                if (_cachedName == null)
                    _cachedName = LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(nameIndex);

                return _cachedName;
            }
        }

        public string? FullName
        {
            get
            {
                if (LibCpp2IlMain.TheMetadata == null)
                    return null;

                if (DeclaringType != null)
                    return DeclaringType.FullName + "/" + Name;

                return (string.IsNullOrEmpty(Namespace) ? "" : Namespace + ".") + Name;
            }
        }

        public Il2CppType? RawBaseType => parentIndex == -1 ? null : LibCpp2IlMain.Binary!.GetType(parentIndex);

        public Il2CppTypeReflectionData? BaseType => parentIndex == -1 ? null : LibCpp2ILUtils.GetTypeReflectionData(LibCpp2IlMain.Binary!.GetType(parentIndex));

        public Il2CppFieldDefinition[]? Fields
        {
            get
            {
                if (LibCpp2IlMain.TheMetadata == null)
                    return null;
                
                if (firstFieldIdx < 0 || field_count == 0)
                    return Array.Empty<Il2CppFieldDefinition>();

                var ret = new Il2CppFieldDefinition[field_count];

                Array.Copy(LibCpp2IlMain.TheMetadata.fieldDefs, firstFieldIdx, ret, 0, field_count);

                return ret;
            }
        }

        public FieldAttributes[]? FieldAttributes => Fields?
            .Select(f => f.typeIndex)
            .Select(idx => LibCpp2IlMain.Binary!.GetType(idx))
            .Select(t => (FieldAttributes)t.attrs)
            .ToArray();

        public object?[]? FieldDefaults => Fields?
            .Select((f, idx) => (f.FieldIndex, FieldAttributes![idx]))
            .Select(tuple => (tuple.Item2 & System.Reflection.FieldAttributes.HasDefault) != 0 ? LibCpp2IlMain.TheMetadata!.GetFieldDefaultValueFromIndex(tuple.FieldIndex) : null)
            .Select(def => def == null ? null : LibCpp2ILUtils.GetDefaultValue(def.dataIndex, def.typeIndex))
            .ToArray();

        public Il2CppFieldReflectionData[]? FieldInfos
        {
            get
            {
                var fields = Fields;
                var attributes = FieldAttributes;
                var defaults = FieldDefaults;

                return fields?
                    .Select((t, i) => new Il2CppFieldReflectionData { attributes = attributes![i], field = t, defaultValue = defaults![i] })
                    .ToArray();
            }
        }

        public Il2CppMethodDefinition[]? Methods
        {
            get
            {
                if (LibCpp2IlMain.TheMetadata == null)
                    return null;
                
                if (firstMethodIdx < 0 || method_count == 0)
                    return Array.Empty<Il2CppMethodDefinition>();

                var ret = new Il2CppMethodDefinition[method_count];

                Array.Copy(LibCpp2IlMain.TheMetadata.methodDefs, firstMethodIdx, ret, 0, method_count);

                return ret;
            }
        }

        public Il2CppPropertyDefinition[]? Properties
        {
            get
            {
                if (LibCpp2IlMain.TheMetadata == null)
                    return null;
                
                if (firstPropertyId < 0 || propertyCount == 0)
                    return Array.Empty<Il2CppPropertyDefinition>();

                var ret = new Il2CppPropertyDefinition[propertyCount];

                Array.Copy(LibCpp2IlMain.TheMetadata.propertyDefs, firstPropertyId, ret, 0, propertyCount);
                
                return ret.Select(p =>
                {
                    p.DeclaringType = this;
                    return p;
                }).ToArray();
            }
        }

        public Il2CppEventDefinition[]? Events => LibCpp2IlMain.TheMetadata == null
            ? null
            : LibCpp2IlMain.TheMetadata.eventDefs.Skip(firstEventId).Take(eventCount).Select(e =>
            {
                e.DeclaringType = this;
                return e;
            }).ToArray();

        public Il2CppTypeDefinition[]? NestedTypes => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.nestedTypeIndices.Skip(nestedTypesStart).Take(nested_type_count).Select(idx => LibCpp2IlMain.TheMetadata.typeDefs[idx]).ToArray();

        public Il2CppType[] RawInterfaces => LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.Binary == null
            ? Array.Empty<Il2CppType>()
            : LibCpp2IlMain.TheMetadata.interfaceIndices
                .Skip(interfacesStart)
                .Take(interfaces_count)
                .Select(idx => LibCpp2IlMain.Binary.GetType(idx))
                .ToArray();

        public Il2CppTypeReflectionData[]? Interfaces => LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.Binary == null
            ? null
            : RawInterfaces
                .Select(LibCpp2ILUtils.GetTypeReflectionData)
                .ToArray();

        public Il2CppTypeDefinition? DeclaringType => LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.Binary == null || declaringTypeIndex < 0 ? null : LibCpp2IlMain.TheMetadata.typeDefs[LibCpp2IlMain.Binary.GetType(declaringTypeIndex).data.classIndex];

        public Il2CppGenericContainer? GenericContainer => genericContainerIndex < 0 ? null : LibCpp2IlMain.TheMetadata?.genericContainers[genericContainerIndex];

        public override string ToString()
        {
            if (LibCpp2IlMain.TheMetadata == null) return base.ToString();

            return $"Il2CppTypeDefinition[namespace='{Namespace}', name='{Name}', parentType={BaseType?.ToString() ?? "null"}]";
        }
    }
}