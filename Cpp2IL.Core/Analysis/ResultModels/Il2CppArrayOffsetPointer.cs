namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class Il2CppArrayOffsetPointer<T>
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