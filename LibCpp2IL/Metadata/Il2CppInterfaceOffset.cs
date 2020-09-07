using LibCpp2IL.PE;

namespace LibCpp2IL.Metadata
{
    public class Il2CppInterfaceOffset
    {
        public int typeIndex;
        public int offset;

        public Il2CppTypeDefinition? type => LibCpp2IlReflection.GetTypeDefinitionByTypeIndex(typeIndex);

        public override string ToString()
        {
            return $"InterfaceOffsetPair({typeIndex}/{type?.FullName ?? "unknown type"} => {offset})";
        }
    }
}