using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata;

public class Il2CppParameterDefaultValue : ReadableClass
{
    public Il2CppVariableWidthIndex<Il2CppParameterDefinition> parameterIndex;
    public Il2CppVariableWidthIndex<Il2CppType> typeIndex;
    public Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy> dataIndex;

    public object? ContainedDefaultValue => LibCpp2ILUtils.GetDefaultValue(dataIndex, typeIndex, OwningContext);

    public override void Read(ClassReadingBinaryReader reader)
    {
        parameterIndex = Il2CppVariableWidthIndex<Il2CppParameterDefinition>.Read(reader);
        typeIndex = Il2CppVariableWidthIndex<Il2CppType>.Read(reader);
        dataIndex = Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy>.Read(reader);
    }
}
