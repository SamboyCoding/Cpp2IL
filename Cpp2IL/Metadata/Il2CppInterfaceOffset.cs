using Mono.Cecil;

namespace Cpp2IL.Metadata
{
    public class Il2CppInterfaceOffset
    {
        public int typeIndex;
        public int offset;

        public TypeDefinition type => SharedState.TypeDefsByIndex[typeIndex];
    }
}