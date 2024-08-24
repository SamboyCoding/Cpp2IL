using System;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata;

public class Il2CppFieldDefinition : ReadableClass
{
    public int nameIndex;
    public int typeIndex;
    [Version(Max = 24)] public int customAttributeIndex;
    public uint token;

    public string? Name { get; private set; }

    public Il2CppType? RawFieldType => LibCpp2IlMain.Binary?.GetType(typeIndex);
    public Il2CppTypeReflectionData? FieldType => RawFieldType == null ? null : LibCpp2ILUtils.GetTypeReflectionData(RawFieldType);

    public int FieldIndex => LibCpp2IlReflection.GetFieldIndexFromField(this);

    public Il2CppFieldDefaultValue? DefaultValue => LibCpp2IlMain.TheMetadata?.GetFieldDefaultValue(this);

    public override string? ToString()
    {
        if (LibCpp2IlMain.TheMetadata == null)
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
            var (dataIndex, _) = LibCpp2IlMain.TheMetadata!.GetFieldDefaultValue(FieldIndex);

            var pointer = LibCpp2IlMain.TheMetadata!.GetDefaultValueFromIndex(dataIndex);

            if (pointer <= 0) return [];

            var results = LibCpp2IlMain.TheMetadata.ReadByteArrayAtRawAddress(pointer, length);

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

        typeIndex = reader.ReadInt32();
        if (IsAtMost(24f))
            customAttributeIndex = reader.ReadInt32();
        token = reader.ReadUInt32();
    }
}
