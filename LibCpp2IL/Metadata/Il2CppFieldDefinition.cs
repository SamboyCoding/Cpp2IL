using System;
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

    public Il2CppType? RawFieldType => OwningBinary?.GetType(typeIndex);
    public Il2CppTypeReflectionData? FieldType => RawFieldType == null ? null : LibCpp2ILUtils.GetTypeReflectionData(RawFieldType);

    public Il2CppVariableWidthIndex<Il2CppFieldDefinition> FieldIndex => LibCpp2IlReflection.GetFieldIndexFromField(this);

    public Il2CppFieldDefaultValue? DefaultValue => OwningMetadata?.GetFieldDefaultValue(this);

    public Il2CppTypeDefinition DeclaringType => LibCpp2IlReflection.GetDeclaringTypeFromField(this);

    public override string? ToString()
    {
        if (OwningMetadata == null)
            return base.ToString();

        return $"Il2CppFieldDefinition[Name={Name}, FieldType={FieldType}]";
    }

    public byte[] StaticArrayInitialValue
    {
        get
        {
            if (FieldType is not { isArray: false, isPointer: false, isType: true, isGenericType: false })
                return [];

            if (FieldType.baseType!.Name?.StartsWith("__StaticArrayInitTypeSize=") != true)
                return [];

            var length = int.Parse(FieldType.baseType!.Name.Replace("__StaticArrayInitTypeSize=", ""));
            var (dataIndex, _) = OwningMetadata!.GetFieldDefaultValue(FieldIndex);

            var pointer = OwningMetadata!.GetDefaultValueFromIndex(dataIndex);

            if (pointer <= 0) return [];

            var results = OwningMetadata.ReadByteArrayAtRawAddress(pointer, length);

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
