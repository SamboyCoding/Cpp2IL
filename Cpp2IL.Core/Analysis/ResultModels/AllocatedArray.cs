using System.Collections.Generic;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class AllocatedArray
    {
        public ArrayType ArrayType;
        public int Size;
        public readonly Dictionary<int, object?> KnownValuesAtOffsets = new Dictionary<int, object?>();

        public AllocatedArray(int size, ArrayType arrayType)
        {
            Size = size;
            ArrayType = arrayType;
        }
    }
}