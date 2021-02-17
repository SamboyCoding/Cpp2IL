using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class AllocatedArray
    {
        public ArrayType ArrayType;
        public int Size;

        public AllocatedArray(int size, ArrayType arrayType)
        {
            Size = size;
            ArrayType = arrayType;
        }
    }
}