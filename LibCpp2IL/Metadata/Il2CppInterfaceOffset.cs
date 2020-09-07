using LibCpp2IL.PE;

namespace LibCpp2IL.Metadata
{
    public class Il2CppInterfaceOffset
    {
        public int typeIndex;
        public int offset;

        public Il2CppType type => LibCpp2IlMain.ThePe!.types[typeIndex];

        // public TypeDefinition TypeDefinition
        // {
        //     get
        //     {
        //         if(SharedState.TypeDefsByIndex.ContainsKey(type.data.classIndex))
        //             return SharedState.TypeDefsByIndex[type.data.classIndex];
        //         return null;
        //     }
        // }

        public override string ToString()
        {
            // return $"InterfaceOffsetPair({typeIndex}/{TypeDefinition?.FullName ?? "unknown type"} => {offset})";
            return $"InterfaceOffsetPair({typeIndex}/unknown type => {offset})";
        }
    }
}