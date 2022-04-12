
namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppGenericMethodFunctionsDefinitions : ReadableClass
    {
        public int genericMethodIndex;
        public Il2CppGenericMethodIndices indices;
        public override void Read(ClassReadingBinaryReader reader)
        {
            genericMethodIndex = reader.ReadInt32();
            indices = reader.ReadReadableHereNoLock<Il2CppGenericMethodIndices>();
        }
    }
}