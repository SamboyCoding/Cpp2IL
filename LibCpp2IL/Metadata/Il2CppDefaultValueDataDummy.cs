namespace LibCpp2IL.Metadata;

/// <summary>
/// Empty class to represent the data index of a default value, used by <see cref="Il2CppFieldDefaultValue"/> and <see cref="Il2CppParameterDefaultValue"/>, so they can be represented in Il2CppVariableLengthIndex fields.
/// </summary>
public class Il2CppDefaultValueDataDummy : ReadableClass
{
    //[this type intentionally left blank]
    
    public override void Read(ClassReadingBinaryReader reader) { }
}
