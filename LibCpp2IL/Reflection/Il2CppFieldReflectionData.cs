using System.Reflection;
using LibCpp2IL.Metadata;

namespace LibCpp2IL.Reflection;

public class Il2CppFieldReflectionData
{
    public Il2CppFieldDefinition Field;
    public FieldAttributes Attributes;
    public object? DefaultValue;
    public int IndexInParent;
    public int FieldOffset;

    public Il2CppFieldReflectionData(Il2CppFieldDefinition field, FieldAttributes attributes, object? defaultValue, int indexInParent, int fieldOffset)
    {
            Field = field;
            Attributes = attributes;
            DefaultValue = defaultValue;
            IndexInParent = indexInParent;
            FieldOffset = fieldOffset;
        }
}