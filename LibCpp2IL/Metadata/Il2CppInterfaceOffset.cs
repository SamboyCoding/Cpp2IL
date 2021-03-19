using LibCpp2IL.PE;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata
{
    public class Il2CppInterfaceOffset
    {
        public int typeIndex;
        public int offset;

        public Il2CppTypeReflectionData? type => LibCpp2ILUtils.GetTypeReflectionData(LibCpp2IlMain.ThePe!.GetType(typeIndex));

        public override string ToString()
        {
            return $"InterfaceOffsetPair({typeIndex}/{type?.ToString() ?? "unknown type"} => {offset})";
        }
    }
}