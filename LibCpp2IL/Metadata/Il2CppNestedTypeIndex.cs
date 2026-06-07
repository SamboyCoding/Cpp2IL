namespace LibCpp2IL.Metadata;

/// <summary>
/// Strongly-typed wrapper around an int so that we can specify a width for it in Il2CppVariableWidthIndex
/// </summary>
public class Il2CppNestedTypeIndex : ReadableClass
{
    public int Value { get; private set; }
    
    public override void Read(ClassReadingBinaryReader reader)
    {
        Value = reader.ReadInt32();
    }
}
