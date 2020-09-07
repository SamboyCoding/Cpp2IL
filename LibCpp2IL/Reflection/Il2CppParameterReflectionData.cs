using System.Reflection;

namespace LibCpp2IL.Reflection
{
    public class Il2CppParameterReflectionData
    {
        public string ParameterName;
        public Il2CppTypeReflectionData Type;
        public ParameterAttributes ParameterAttributes;
        public object? DefaultValue;
    }
}