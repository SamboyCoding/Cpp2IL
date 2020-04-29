using Cpp2IL.PE;
using Mono.Cecil;

namespace Cpp2IL.Metadata
{
    public class Il2CppInterfaceOffset
    {
        public int typeIndex;
        public int offset;

        public Il2CppType type => Program.ThePE.types[typeIndex];

        public TypeDefinition TypeDefinition => SharedState.TypeDefsByIndex[type.data.classIndex];
    }
}