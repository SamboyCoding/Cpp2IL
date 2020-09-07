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

        public enum Type
        {
            TYPEREF,
            METHODREF,
            FIELDREF,
            LITERAL
        }
    }
}