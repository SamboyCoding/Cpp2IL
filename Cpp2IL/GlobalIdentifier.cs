namespace Cpp2IL
{
    internal static partial class AssemblyBuilder
    {
        internal struct GlobalIdentifier
        {
            public ulong Offset;
            public string Name;
            public Type IdentifierType;

            public override string ToString()
            {
                return $"Cpp2IL Global Identifier (Name = {Name}, Offset = 0x{Offset:X}, Type = {IdentifierType})";
            }

            public enum Type
            {
                TYPE,
                METHOD,
                FIELD,
                LITERAL
            }
        }
    }
}