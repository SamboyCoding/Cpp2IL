using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata;

/// <summary>
/// Used by metadata usages
/// </summary>
public class Il2CppFieldRef : ReadableClass
{
    public Il2CppVariableWidthIndex<Il2CppType> typeIndex;
    public Il2CppVariableWidthIndex<Il2CppFieldDefinition> fieldIndex; // local offset into type fields

    public Il2CppType? DeclaringType => OwningContext.Binary.GetType(typeIndex);

    public Il2CppTypeDefinition? DeclaringTypeDefinition => OwningContext.Metadata.GetTypeDefinitionFromIndex(DeclaringType!.Data.ClassIndex);

    public Il2CppFieldDefinition? FieldDefinition => OwningContext.Metadata.GetFieldDefinitionFromIndex(DeclaringTypeDefinition!.FirstFieldIdx + fieldIndex);

    public override void Read(ClassReadingBinaryReader reader)
    {
        typeIndex = Il2CppVariableWidthIndex<Il2CppType>.Read(reader);
        fieldIndex = Il2CppVariableWidthIndex<Il2CppFieldDefinition>.Read(reader);
    }
}
