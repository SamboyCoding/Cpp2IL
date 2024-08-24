namespace LibCpp2IL.BinaryStructures;

public class Il2CppGenericMethodFunctionsDefinitions : ReadableClass
{
    public int GenericMethodIndex;
    public Il2CppGenericMethodIndices Indices = null!;

    public override void Read(ClassReadingBinaryReader reader)
    {
        GenericMethodIndex = reader.ReadInt32();
        Indices = reader.ReadReadableHereNoLock<Il2CppGenericMethodIndices>();
    }
}
