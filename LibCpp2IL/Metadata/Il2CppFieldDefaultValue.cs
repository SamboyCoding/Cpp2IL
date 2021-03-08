namespace LibCpp2IL.Metadata
{
    public class Il2CppFieldDefaultValue
    {
        public int fieldIndex;
        public int typeIndex;
        public int dataIndex;
        
        public object? Value => dataIndex <= 0 ? null : LibCpp2ILUtils.GetDefaultValue(dataIndex, typeIndex);
    }
}