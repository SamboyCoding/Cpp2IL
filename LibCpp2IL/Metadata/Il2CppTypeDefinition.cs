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
    public Il2CppVariableWidthIndex<Il2CppType> ByvalTypeIndex;

    [Version(Max = 24.5f)] //Removed in v27
    public int ByrefTypeIndex;

    public Il2CppVariableWidthIndex<Il2CppType> DeclaringTypeIndex;
    public Il2CppVariableWidthIndex<Il2CppType> ParentIndex;

    [Version(Max = 34f)] //Removed in v35
    public int ElementTypeIndex; // we can probably remove this one. Only used for enums

    [Version(Max = 24.15f)] public int RgctxStartIndex;
    [Version(Max = 24.15f)] public int RgctxCount;

    public Il2CppVariableWidthIndex<Il2CppGenericContainer> GenericContainerIndex;

    public uint Flags;

    public Il2CppVariableWidthIndex<Il2CppFieldDefinition> FirstFieldIdx;
    public Il2CppVariableWidthIndex<Il2CppMethodDefinition> FirstMethodIdx;
    public Il2CppVariableWidthIndex<Il2CppEventDefinition> FirstEventId;
    public Il2CppVariableWidthIndex<Il2CppPropertyDefinition> FirstPropertyId;
    public Il2CppVariableWidthIndex<Il2CppNestedTypeIndex> NestedTypesStart;
    public Il2CppVariableWidthIndex<Il2CppInterfaceOffset> InterfacesStart;
    public int VtableStart;
    public Il2CppVariableWidthIndex<Il2CppInterfaceOffset> InterfaceOffsetsStart;

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
    // 17 - is_byref_like
    // 18 - has_inline_array
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
    public bool HasInlineArray => (Bitfield >> 17 & 0x1) == 1; //technically v104 and above but we don't use this anywhere yet.

    public TypeAttributes Attributes => (TypeAttributes)Flags;

    public Il2CppType RawType => OwningContext.Binary.GetType(ByvalTypeIndex);

    public Il2CppTypeDefinitionSizes RawSizes
    {
        get
        {
            var sizePtr = OwningContext.Binary.TypeDefinitionSizePointers[TypeIndex.Value];
            return OwningContext.Binary.ReadReadableAtVirtualAddress<Il2CppTypeDefinitionSizes>(sizePtr);
        }
    }

    public int Size => RawSizes.native_size;

    public Il2CppInterfaceOffset[] InterfaceOffsets
    {
        get
        {
            if (InterfaceOffsetsStart.IsNull) return [];

            return OwningContext.Metadata.GetInterfaceOffsetsFromIndexAndCount(InterfaceOffsetsStart, InterfaceOffsetsCount);
        }
    }

    public MetadataUsage?[] VTable
    {
        get
        {
            if (VtableStart < 0) return [];

            return OwningContext.Metadata.VTableMethodIndices.SubArray(VtableStart, VtableCount).Select(v => MetadataUsage.DecodeMetadataUsage(v, 0, OwningContext)).ToArray();
        }
    }

    public Il2CppVariableWidthIndex<Il2CppTypeDefinition> TypeIndex => OwningContext.ReflectionCache.GetTypeIndexFromType(this);

    public bool IsAbstract => ((TypeAttributes)Flags & TypeAttributes.Abstract) != 0;

    public bool IsInterface => ((TypeAttributes)Flags & TypeAttributes.Interface) != 0;

    private Il2CppImageDefinition? _cachedDeclaringAssembly;

    public Il2CppImageDefinition? DeclaringAssembly
    {
        get
        {
            if (_cachedDeclaringAssembly == null)
            {
                LibCpp2ILUtils.PopulateDeclaringAssemblyCache(OwningContext.Metadata);
            }

            return _cachedDeclaringAssembly;
        }
        internal set => _cachedDeclaringAssembly = value;
    }

    public Il2CppCodeGenModule? CodeGenModule => OwningContext.Binary.GetCodegenModuleByName(DeclaringAssembly!.Name!);

    public Il2CppRGCTXDefinition[] RgctXs
    {
        get
        {
            if (MetadataVersion < 24.2f)
            {
                //No codegen modules here.
                return OwningContext.Metadata.RgctxDefinitions!.Skip(RgctxStartIndex).Take(RgctxCount).ToArray();
            }

            var cgm = CodeGenModule;

            if (cgm == null)
                return [];

            var rangePair = cgm.RGCTXRanges.FirstOrDefault(r => r.token == Token);

            if (rangePair == null)
                return [];

            return OwningContext.Binary.GetRgctxDataForPair(cgm, rangePair);
        }
    }

    public ulong[] RgctxMethodPointers
    {
        get
        {
            var index = OwningContext.Binary.GetCodegenModuleIndexByName(DeclaringAssembly!.Name!);

            if (index < 0)
                return [];

            var pointers = OwningContext.Binary.GetCodegenModuleMethodPointers(index);

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
                _cachedNamespace = OwningContext.Metadata.GetStringFromIndex(NamespaceIndex);

            return _cachedNamespace;
        }
    }

    private string? _cachedName;

    public string? Name
    {
        get
        {
            if (_cachedName == null)
                _cachedName = OwningContext.Metadata.GetStringFromIndex(NameIndex);

            return _cachedName;
        }
    }

    public string? FullName
    {
        get
        {
            if (DeclaringType != null)
                return $"{DeclaringType.FullName}+{Name}";

            return $"{(string.IsNullOrEmpty(Namespace) ? "" : $"{Namespace}.")}{Name}";
        }
    }

    public Il2CppType? RawBaseType => ParentIndex.IsNull ? null : OwningContext.Binary.GetType(ParentIndex);

    public Il2CppTypeReflectionData? BaseType => ParentIndex.IsNull ? null : LibCpp2ILUtils.GetTypeReflectionData(OwningContext.Binary.GetType(ParentIndex));

    public Il2CppFieldDefinition[]? Fields
    {
        get
        {
            if (FirstFieldIdx.IsNull || FieldCount == 0)
                return [];

            return OwningContext.Metadata.GetFieldDefinitionsFromIndexAndCount(FirstFieldIdx, FieldCount);
        }
    }

    public FieldAttributes[]? FieldAttributes => Fields?
        .Select(f => f.typeIndex)
        .Select(idx => OwningContext.Binary.GetType(idx))
        .Select(t => (FieldAttributes)t.Attrs)
        .ToArray();

    public object?[]? FieldDefaults => Fields?
        .Select((f, idx) => (f.FieldIndex, FieldAttributes![idx]))
        .Select(tuple => (tuple.Item2 & System.Reflection.FieldAttributes.HasDefault) != 0 ? OwningContext.Metadata.GetFieldDefaultValueFromIndex(tuple.FieldIndex) : null)
        .Select(def => def == null ? null : LibCpp2ILUtils.GetDefaultValue(def.dataIndex, def.typeIndex, OwningContext))
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
                    OwningContext.Binary.GetFieldOffsetFromIndex(TypeIndex, i, fields[i].FieldIndex, IsValueType, attributes[i].HasFlag(System.Reflection.FieldAttributes.Static))
                );
            }

            return ret;
        }
    }

    public Il2CppMethodDefinition[]? Methods
    {
        get
        {
            if (FirstMethodIdx.IsNull || MethodCount == 0)
                return [];

            return OwningContext.Metadata.GetMethodDefinitionsFromIndexAndCount(FirstMethodIdx, MethodCount);
        }
    }

    public Il2CppPropertyDefinition[]? Properties
    {
        get
        {
            if (FirstPropertyId.IsNull || PropertyCount == 0)
                return [];

            var ret = OwningContext.Metadata.GetPropertyDefinitionsFromIndexAndCount(FirstPropertyId, PropertyCount);

            foreach (var definition in ret)
                definition.DeclaringType = this;

            return ret;
        }
    }

    public Il2CppEventDefinition[]? Events
    {
        get
        {
            if (FirstEventId.IsNull || EventCount == 0)
                return [];

            var ret = OwningContext.Metadata.GetEventDefinitionsFromIndexAndCount(FirstEventId, EventCount);
            foreach (var def in ret)
                def.DeclaringType = this;

            return ret;
        }
    }

    public Il2CppTypeDefinition[]? NestedTypes => OwningContext.Metadata.GetNestedTypeIndicesFromIndexAndCount(NestedTypesStart, NestedTypeCount)
            .Select(Il2CppVariableWidthIndex<Il2CppTypeDefinition>.MakeTemporaryForFixedWidthUsage) //DynWidth: nestedTypeIndices is always int, so making temp is ok
            .Select(OwningContext.Metadata.GetTypeDefinitionFromIndex)
            .ToArray();

    public Il2CppType[] RawInterfaces => OwningContext.Metadata.GetInterfaceIndicesFromIndexAndCount(InterfacesStart, InterfacesCount)
            .Select(OwningContext.Binary.GetType)
            .ToArray();

    public Il2CppTypeReflectionData[]? Interfaces => RawInterfaces
            .Select(LibCpp2ILUtils.GetTypeReflectionData)
            .ToArray();

    public Il2CppTypeDefinition? DeclaringType => DeclaringTypeIndex.IsNull ? null : OwningContext.Binary.GetType(DeclaringTypeIndex).CoerceToUnderlyingTypeDefinition();

    public Il2CppTypeDefinition? ElementType => ElementTypeIndex < 0
        ? null
        : OwningContext.Binary.GetType(Il2CppVariableWidthIndex<Il2CppType>.MakeTemporaryForFixedWidthUsage(ElementTypeIndex)).CoerceToUnderlyingTypeDefinition(); //DynWidth: ElementTypeIndex was removed in v35, so it's never dynamic

    public Il2CppGenericContainer? GenericContainer => GenericContainerIndex.IsNull ? null : OwningContext.Metadata.GetGenericContainerFromIndex(GenericContainerIndex);

    public Il2CppType EnumUnderlyingType
    {
        get
        {
            if (!IsEnumType)
                throw new InvalidOperationException("Cannot get the underlying type of a non-enum type.");

            if (IsAtLeast(35f))
                //v35: ElementTypeIndex removed, enum base type is just normal base type
                return RawBaseType!;

            //pre-v35: ElementTypeIndex is used for enums to store the underlying type, so we need to get the type from there instead of the parent index (which is just System.Enum)
            return OwningContext.Binary.GetType(Il2CppVariableWidthIndex<Il2CppType>.MakeTemporaryForFixedWidthUsage(ElementTypeIndex));
        }
    }

    public override string? ToString()
    {
        return $"Il2CppTypeDefinition[namespace='{Namespace}', name='{Name}', parentType={BaseType?.ToString() ?? "null"}, assembly={DeclaringAssembly}]";
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        NameIndex = reader.ReadInt32();
        NamespaceIndex = reader.ReadInt32();

        if (IsAtMost(24f))
            CustomAttributeIndex = reader.ReadInt32();

        ByvalTypeIndex = Il2CppVariableWidthIndex<Il2CppType>.Read(reader);

        if (IsLessThan(27f))
            ByrefTypeIndex = reader.ReadInt32();

        DeclaringTypeIndex = Il2CppVariableWidthIndex<Il2CppType>.Read(reader);
        ParentIndex = Il2CppVariableWidthIndex<Il2CppType>.Read(reader);

        if(IsLessThan(35f))
            ElementTypeIndex = reader.ReadInt32();

        if (IsAtMost(24.15f))
        {
            RgctxStartIndex = reader.ReadInt32();
            RgctxCount = reader.ReadInt32();
        }

        GenericContainerIndex = Il2CppVariableWidthIndex<Il2CppGenericContainer>.Read(reader);
        Flags = reader.ReadUInt32();

        FirstFieldIdx = Il2CppVariableWidthIndex<Il2CppFieldDefinition>.Read(reader);
        FirstMethodIdx = Il2CppVariableWidthIndex<Il2CppMethodDefinition>.Read(reader);
        FirstEventId = Il2CppVariableWidthIndex<Il2CppEventDefinition>.Read(reader);
        FirstPropertyId = Il2CppVariableWidthIndex<Il2CppPropertyDefinition>.Read(reader);
        NestedTypesStart = Il2CppVariableWidthIndex<Il2CppNestedTypeIndex>.Read(reader);
        InterfacesStart = Il2CppVariableWidthIndex<Il2CppInterfaceOffset>.Read(reader);
        VtableStart = reader.ReadInt32();
        InterfaceOffsetsStart = Il2CppVariableWidthIndex<Il2CppInterfaceOffset>.Read(reader);

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
