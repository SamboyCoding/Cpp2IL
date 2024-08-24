using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata;

/// <summary>
/// Used by metadata usages
/// </summary>
public class Il2CppFieldRef : ReadableClass
{
    public int typeIndex;
    public int fieldIndex; // local offset into type fields

    public Il2CppType? DeclaringType => LibCpp2IlMain.Binary?.GetType(typeIndex);

    public Il2CppTypeDefinition? DeclaringTypeDefinition => LibCpp2IlMain.TheMetadata?.typeDefs[DeclaringType!.Data.ClassIndex];

    public Il2CppFieldDefinition? FieldDefinition => LibCpp2IlMain.TheMetadata?.fieldDefs[DeclaringTypeDefinition!.FirstFieldIdx + fieldIndex];

    public override void Read(ClassReadingBinaryReader reader)
    {
        typeIndex = reader.ReadInt32();
        fieldIndex = reader.ReadInt32();
    }
}
