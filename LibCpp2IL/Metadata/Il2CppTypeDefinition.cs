using System;
using System.Linq;
using System.Reflection;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata;

public class Il2CppTypeDefinition : ReadableClass
{
    public int NameIndex;
    public int NamespaceIndex;
    [Version(Max = 24)] public int CustomAttributeIndex;
    public int ByvalTypeIndex;

    [Version(Max = 24.5f)] //Removed in v27 
    public int ByrefTypeIndex;

    public int DeclaringTypeIndex;
    public int ParentIndex;
    public int ElementTypeIndex; // we can probably remove this one. Only used for enums

    [Version(Max = 24.15f)] public int RgctxStartIndex;
    [Version(Max = 24.15f)] public int RgctxCount;

    public int GenericContainerIndex;

    public uint Flags;

    public int FirstFieldIdx;
    public int FirstMethodIdx;
    public int FirstEventId;
    public int FirstPropertyId;
    public int NestedTypesStart;
    public int InterfacesStart;
    public int VtableStart;
    public int InterfaceOffsetsStart;

    public ushort MethodCount;
    public ushort PropertyCount;
    public ushort FieldCount;
    public ushort EventCount;
    public ushort NestedTypeCount;
    public ushort VtableCount;
    public ushort InterfacesCount;
    public ushort InterfaceOffsetsCount;

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
    public uint Bitfield;
    public uint Token;

    public bool IsValueType => (Bitfield >> 0 & 0x1) == 1;
    public bool IsEnumType => (Bitfield >> 1 & 0x1) == 1;
    public bool HasFinalizer => (Bitfield >> 2 & 0x1) == 1;
    public bool HasCctor => (Bitfield >> 3 & 0x1) == 1;
    public bool IsBlittable => (Bitfield >> 4 & 0x1) == 1;
    public bool IsImportOrWindowsRuntime => (Bitfield >> 5 & 0x1) == 1;
    public uint PackingSize => ((Il2CppPackingSizeEnum)(Bitfield >> 6 & 0xF)).NumericalValue();
    public bool PackingSizeIsDefault => (Bitfield >> 10 & 0x1) == 1;
    public bool ClassSizeIsDefault => (Bitfield >> 11 & 0x1) == 1;
    public uint SpecifiedPackingSize => ((Il2CppPackingSizeEnum)(Bitfield >> 12 & 0xF)).NumericalValue();
    public bool IsByRefLike => (Bitfield >> 16 & 0x1) == 1;

    public TypeAttributes Attributes => (TypeAttributes)Flags;

    public Il2CppTypeDefinitionSizes RawSizes
    {
        get
        {
            var sizePtr = LibCpp2IlMain.Binary!.TypeDefinitionSizePointers[TypeIndex];
            return LibCpp2IlMain.Binary.ReadReadableAtVirtualAddress<Il2CppTypeDefinitionSizes>(sizePtr);
        }
    }

    public int Size => RawSizes.native_size;

    public Il2CppInterfaceOffset[] InterfaceOffsets
    {
        get
        {
            if (InterfaceOffsetsStart < 0) return [];

            return LibCpp2IlMain.TheMetadata!.interfaceOffsets.SubArray(InterfaceOffsetsStart, InterfaceOffsetsCount);
        }
    }

    public MetadataUsage?[] VTable
    {
        get
        {
            if (VtableStart < 0) return [];

            return LibCpp2IlMain.TheMetadata!.VTableMethodIndices.SubArray(VtableStart, VtableCount).Select(v => MetadataUsage.DecodeMetadataUsage(v, 0)).ToArray();
        }
    }

    public int TypeIndex => LibCpp2IlReflection.GetTypeIndexFromType(this);

    public bool IsAbstract => ((TypeAttributes)Flags & TypeAttributes.Abstract) != 0;

    public bool IsInterface => ((TypeAttributes)Flags & TypeAttributes.Interface) != 0;

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

    public Il2CppRGCTXDefinition[] RgctXs
    {
        get
        {
            if (LibCpp2IlMain.MetadataVersion < 24.2f)
            {
                //No codegen modules here.
                return LibCpp2IlMain.TheMetadata!.RgctxDefinitions.Skip(RgctxStartIndex).Take(RgctxCount).ToArray();
            }

            var cgm = CodeGenModule;

            if (cgm == null)
                return [];

            var rangePair = cgm.RGCTXRanges.FirstOrDefault(r => r.token == Token);

            if (rangePair == null)
                return [];

            return LibCpp2IlMain.Binary!.GetRgctxDataForPair(cgm, rangePair);
        }
    }

    public ulong[] RgctxMethodPointers
    {
        get
        {
            var index = LibCpp2IlMain.Binary!.GetCodegenModuleIndexByName(DeclaringAssembly!.Name!);

            if (index < 0)
                return [];

            var pointers = LibCpp2IlMain.Binary!.GetCodegenModuleMethodPointers(index);

            return RgctXs
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
                _cachedNamespace = LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(NamespaceIndex);

            return _cachedNamespace;
        }
    }

    private string? _cachedName;

    public string? Name
    {
        get
        {
            if (_cachedName == null)
                _cachedName = LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(NameIndex);

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
                return $"{DeclaringType.FullName}/{Name}";

            return $"{(string.IsNullOrEmpty(Namespace) ? "" : $"{Namespace}.")}{Name}";
        }
    }

    public Il2CppType? RawBaseType => ParentIndex == -1 ? null : LibCpp2IlMain.Binary!.GetType(ParentIndex);

    public Il2CppTypeReflectionData? BaseType => ParentIndex == -1 || LibCpp2IlMain.Binary == null ? null : LibCpp2ILUtils.GetTypeReflectionData(LibCpp2IlMain.Binary!.GetType(ParentIndex));

    public Il2CppFieldDefinition[]? Fields
    {
        get
        {
            if (LibCpp2IlMain.TheMetadata == null)
                return null;

            if (FirstFieldIdx < 0 || FieldCount == 0)
                return [];

            var ret = new Il2CppFieldDefinition[FieldCount];

            Array.Copy(LibCpp2IlMain.TheMetadata.fieldDefs, FirstFieldIdx, ret, 0, FieldCount);

            return ret;
        }
    }

    public FieldAttributes[]? FieldAttributes => Fields?
        .Select(f => f.typeIndex)
        .Select(idx => LibCpp2IlMain.Binary!.GetType(idx))
        .Select(t => (FieldAttributes)t.Attrs)
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

            if (fields == null || attributes == null || defaults == null)
                return null;

            var ret = new Il2CppFieldReflectionData[FieldCount];
            for (var i = 0; i < FieldCount; i++)
            {
                ret[i] = new(
                    fields[i],
                    attributes![i],
                    defaults![i],
                    i,
                    LibCpp2IlMain.Binary!.GetFieldOffsetFromIndex(TypeIndex, i, fields[i].FieldIndex, IsValueType, attributes[i].HasFlag(System.Reflection.FieldAttributes.Static))
                );
            }

            return ret;
        }
    }

    public Il2CppMethodDefinition[]? Methods
    {
        get
        {
            if (LibCpp2IlMain.TheMetadata == null)
                return null;

            if (FirstMethodIdx < 0 || MethodCount == 0)
                return [];

            var ret = new Il2CppMethodDefinition[MethodCount];

            Array.Copy(LibCpp2IlMain.TheMetadata.methodDefs, FirstMethodIdx, ret, 0, MethodCount);

            return ret;
        }
    }

    public Il2CppPropertyDefinition[]? Properties
    {
        get
        {
            if (LibCpp2IlMain.TheMetadata == null)
                return null;

            if (FirstPropertyId < 0 || PropertyCount == 0)
                return [];

            var ret = new Il2CppPropertyDefinition[PropertyCount];

            Array.Copy(LibCpp2IlMain.TheMetadata.propertyDefs, FirstPropertyId, ret, 0, PropertyCount);

            return ret.Select(p =>
            {
                p.DeclaringType = this;
                return p;
            }).ToArray();
        }
    }

    public Il2CppEventDefinition[]? Events => LibCpp2IlMain.TheMetadata == null
        ? null
        : LibCpp2IlMain.TheMetadata.eventDefs.Skip(FirstEventId).Take(EventCount).Select(e =>
        {
            e.DeclaringType = this;
            return e;
        }).ToArray();

    public Il2CppTypeDefinition[]? NestedTypes => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.nestedTypeIndices.Skip(NestedTypesStart).Take(NestedTypeCount).Select(idx => LibCpp2IlMain.TheMetadata.typeDefs[idx]).ToArray();

    public Il2CppType[] RawInterfaces => LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.Binary == null
        ? []
        : LibCpp2IlMain.TheMetadata.interfaceIndices
            .Skip(InterfacesStart)
            .Take(InterfacesCount)
            .Select(idx => LibCpp2IlMain.Binary.GetType(idx))
            .ToArray();

    public Il2CppTypeReflectionData[]? Interfaces => LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.Binary == null
        ? null
        : RawInterfaces
            .Select(LibCpp2ILUtils.GetTypeReflectionData)
            .ToArray();

    public Il2CppTypeDefinition? DeclaringType => LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.Binary == null || DeclaringTypeIndex < 0 ? null : LibCpp2IlMain.TheMetadata.typeDefs[LibCpp2IlMain.Binary.GetType(DeclaringTypeIndex).Data.ClassIndex];

    public Il2CppGenericContainer? GenericContainer => GenericContainerIndex < 0 ? null : LibCpp2IlMain.TheMetadata?.genericContainers[GenericContainerIndex];

    public Il2CppType EnumUnderlyingType => IsEnumType ? LibCpp2IlMain.Binary!.GetType(ElementTypeIndex) : throw new InvalidOperationException("Cannot get the underlying type of a non-enum type.");

    public override string? ToString()
    {
        if (LibCpp2IlMain.TheMetadata == null)
            return base.ToString();

        return $"Il2CppTypeDefinition[namespace='{Namespace}', name='{Name}', parentType={BaseType?.ToString() ?? "null"}, assembly={DeclaringAssembly}]";
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        NameIndex = reader.ReadInt32();
        NamespaceIndex = reader.ReadInt32();

        if (IsAtMost(24f))
            CustomAttributeIndex = reader.ReadInt32();

        ByvalTypeIndex = reader.ReadInt32();

        if (IsLessThan(27f))
            ByrefTypeIndex = reader.ReadInt32();

        DeclaringTypeIndex = reader.ReadInt32();
        ParentIndex = reader.ReadInt32();
        ElementTypeIndex = reader.ReadInt32();

        if (IsAtMost(24.15f))
        {
            RgctxStartIndex = reader.ReadInt32();
            RgctxCount = reader.ReadInt32();
        }

        GenericContainerIndex = reader.ReadInt32();
        Flags = reader.ReadUInt32();

        FirstFieldIdx = reader.ReadInt32();
        FirstMethodIdx = reader.ReadInt32();
        FirstEventId = reader.ReadInt32();
        FirstPropertyId = reader.ReadInt32();
        NestedTypesStart = reader.ReadInt32();
        InterfacesStart = reader.ReadInt32();
        VtableStart = reader.ReadInt32();
        InterfaceOffsetsStart = reader.ReadInt32();

        MethodCount = reader.ReadUInt16();
        PropertyCount = reader.ReadUInt16();
        FieldCount = reader.ReadUInt16();
        EventCount = reader.ReadUInt16();
        NestedTypeCount = reader.ReadUInt16();
        VtableCount = reader.ReadUInt16();
        InterfacesCount = reader.ReadUInt16();
        InterfaceOffsetsCount = reader.ReadUInt16();

        Bitfield = reader.ReadUInt32();
        Token = reader.ReadUInt32();
    }
}
