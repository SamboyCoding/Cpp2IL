namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class Il2CppArrayOffsetPointer<T>
    {
        public LocalDefinition<T> Array;
        public int Offset;

        public Il2CppArrayOffsetPointer(LocalDefinition<T> array, int offset)
        {
            Array = array;
            Offset = offset;
        }
    }
}