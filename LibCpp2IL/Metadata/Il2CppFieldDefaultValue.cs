namespace LibCpp2IL.Metadata;

public class Il2CppFieldDefaultValue : ReadableClass
{
    public int fieldIndex;
    public int typeIndex;
    public int dataIndex;

    public object? Value => dataIndex <= 0 ? null : LibCpp2ILUtils.GetDefaultValue(dataIndex, typeIndex);

    public override void Read(ClassReadingBinaryReader reader)
    {
        fieldIndex = reader.ReadInt32();
        typeIndex = reader.ReadInt32();
        dataIndex = reader.ReadInt32();
    }
}
