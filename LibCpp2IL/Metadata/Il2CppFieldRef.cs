using LibCpp2IL.PE;

namespace LibCpp2IL.Metadata
{
    public class Il2CppFieldRef
    {
        public int typeIndex;
        public int fieldIndex; // local offset into type fields

        public Il2CppType? DeclaringType => LibCpp2IlMain.ThePe?.GetType(typeIndex);

        public Il2CppTypeDefinition? DeclaringTypeDefinition => LibCpp2IlMain.TheMetadata?.typeDefs[DeclaringType!.data.classIndex];

        public Il2CppFieldDefinition? FieldDefinition => LibCpp2IlMain.TheMetadata?.fieldDefs[DeclaringTypeDefinition!.firstFieldIdx + fieldIndex];
    }
}