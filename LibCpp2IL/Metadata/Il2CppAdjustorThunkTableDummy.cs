namespace LibCpp2IL.Metadata;

/// <summary>
/// Empty class to represent the in-binary adjustor thunk table, so that indices into it can be represented as Il2CppVariableWidthIndex fields (v108+).
/// </summary>
public class Il2CppAdjustorThunkTableDummy : ReadableClass
{
    //[this type intentionally left blank]

    public override void Read(ClassReadingBinaryReader reader) { }
}
