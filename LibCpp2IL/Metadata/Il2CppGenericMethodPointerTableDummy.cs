namespace LibCpp2IL.Metadata;

/// <summary>
/// Empty class to represent the in-binary generic method pointer table, so that indices into it can be represented as Il2CppVariableWidthIndex fields (v108+).
/// </summary>
public class Il2CppGenericMethodPointerTableDummy : ReadableClass
{
    //[this type intentionally left blank]

    public override void Read(ClassReadingBinaryReader reader) { }
}
