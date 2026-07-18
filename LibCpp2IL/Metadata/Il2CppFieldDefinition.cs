using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata;

public class Il2CppFieldDefinition : ReadableClass
{
    public int nameIndex;
    public Il2CppVariableWidthIndex<Il2CppType> typeIndex;
    [Version(Max = 24)] public int customAttributeIndex;
    public uint token;

    public string? Name { get; private set; }

    public Il2CppType? RawFieldType => OwningContext.Binary.GetType(typeIndex);
    public Il2CppTypeReflectionData? FieldType => RawFieldType == null ? null : LibCpp2ILUtils.GetTypeReflectionData(RawFieldType);

    public Il2CppVariableWidthIndex<Il2CppFieldDefinition> FieldIndex => OwningContext.ReflectionCache.GetFieldIndexFromField(this);

    public Il2CppFieldDefaultValue? DefaultValue => OwningContext.Metadata.GetFieldDefaultValue(this);

    public Il2CppTypeDefinition DeclaringType => OwningContext.ReflectionCache.GetDeclaringTypeFromField(this);

    public override string? ToString()
    {
        return $"Il2CppFieldDefinition[Name={Name}, FieldType={FieldType}]";
    }

    public byte[] StaticArrayInitialValue
    {
        get
        {
            if (FieldType is not { isArray: false, isPointer: false, isType: true, isGenericType: false })
                return [];

            var (dataIndex, _) = OwningContext.Metadata.GetFieldDefaultValue(FieldIndex);

            if (dataIndex.IsNull) return [];

            var baseType = FieldType.baseType;
            if (baseType == null)
                return [];

            //prefer the N encoded in the type name, as the binary's native_size can be -1 or wrong on some il2cpp versions
            var length = baseType.Size;
            if (baseType.Name?.StartsWith("__StaticArrayInitTypeSize=") == true && int.TryParse(baseType.Name["__StaticArrayInitTypeSize=".Length..], out var parsedLength))
                length = parsedLength;

            if (length <= 0) return [];

            var pointer = OwningContext.Metadata.GetDefaultValueFromIndex(dataIndex);

            if (pointer <= 0) return [];

            var results = OwningContext.Metadata.ReadByteArrayAtRawAddress(pointer, length);

            return results;
        }
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        nameIndex = reader.ReadInt32();

        //Cache name now
        var pos = reader.Position;
        Name = ((Il2CppMetadata)reader).ReadStringFromIndexNoReadLock(nameIndex);
        reader.Position = pos;

        typeIndex = Il2CppVariableWidthIndex<Il2CppType>.Read(reader);
        if (IsAtMost(24f))
            customAttributeIndex = reader.ReadInt32();
        token = reader.ReadUInt32();
    }
}
