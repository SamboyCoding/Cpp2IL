using System.Reflection;
using LibCpp2IL.Metadata;

namespace LibCpp2IL.Reflection
{
    public class Il2CppFieldReflectionData
    {
        public Il2CppFieldDefinition field;
        public FieldAttributes attributes;
        public object? defaultValue;
        public int indexInParent;
    }
}