namespace Cpp2IL.Analysis.ResultModels
{
    public class Il2CppArrayOffsetPointer
    {
        public LocalDefinition Array;
        public int Offset;

        public Il2CppArrayOffsetPointer(LocalDefinition array, int offset)
        {
            Array = array;
            Offset = offset;
        }
    }
}