using System.Reflection;
using LibCpp2IL.Metadata;

namespace LibCpp2IL
{
    public struct Il2CppFieldReflectionData
    {
        public Il2CppFieldDefinition field;
        public FieldAttributes attributes;
        public object? defaultValue;
    }
}