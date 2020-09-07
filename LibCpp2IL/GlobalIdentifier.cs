using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL
{
    public struct GlobalIdentifier
    {
        public ulong Offset;
        public string Name;
        public object Value;
        public Type IdentifierType;

        public override string ToString()
        {
            return $"LibCpp2IL Global Identifier (Name = {Name}, Offset = 0x{Offset:X}, Type = {IdentifierType})";
        }

        public Il2CppFieldDefinition? ReferencedField => IdentifierType != Type.FIELDREF ? null
            : Value is Il2CppFieldDefinition def ? def : null;

        public Il2CppTypeReflectionData? ReferencedType => IdentifierType != Type.TYPEREF ? null
            : Value is Il2CppTypeReflectionData data ? data : null;

        public Il2CppMethodDefinition? ReferencedMethod => IdentifierType != Type.METHODREF ? null
            : Value is Il2CppGlobalGenericMethodRef genericMethodRef ? genericMethodRef.baseMethod //TODO Can we get a specific generic variant?
            : Value is Il2CppMethodDefinition definition ? definition : null;

        public enum Type
        {
            TYPEREF,
            METHODREF,
            FIELDREF,
            LITERAL
        }
    }
}