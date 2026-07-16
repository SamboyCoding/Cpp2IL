using System.Reflection;
using LibCpp2IL.Metadata;

namespace LibCpp2IL.Reflection;

public class Il2CppFieldReflectionData(
    Il2CppFieldDefinition field,
    FieldAttributes attributes,
    ConstantValue? defaultValue,
    int indexInParent,
    int fieldOffset)
{
    public Il2CppFieldDefinition Field = field;
    public FieldAttributes Attributes = attributes;
    public ConstantValue? DefaultValue = defaultValue;
    public int IndexInParent = indexInParent;
    public int FieldOffset = fieldOffset;
}
