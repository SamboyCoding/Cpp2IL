using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.ISIL
{
    public readonly struct IsilTypeMetadataUsageOperand : IsilOperandData
    {
        public readonly Il2CppTypeReflectionData TypeReflectionData;

        public IsilTypeMetadataUsageOperand(Il2CppTypeReflectionData typeReflectionData)
        {
            TypeReflectionData = typeReflectionData;
        }

        public override string ToString() => "typeof("+TypeReflectionData.ToString() + ")";
    }
}
