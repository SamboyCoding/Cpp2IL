using Mono.Cecil;

namespace Cpp2IL
{
    internal partial class AsmDumper
    {
        private class ArrayData
        {
            public readonly ulong Length;
            public readonly TypeDefinition ElementType;

            public ArrayData(ulong length, TypeDefinition elementType)
            {
                Length = length;
                ElementType = elementType;
            }
        }
    }
}