using System.Reflection;
using System.Text;
using LibCpp2IL.BinaryStructures;

#pragma warning disable 8618
namespace LibCpp2IL.Reflection
{
    public class Il2CppParameterReflectionData
    {
        public string ParameterName;
        public Il2CppType RawType;
        public Il2CppTypeReflectionData Type;
        public ParameterAttributes Attributes;
        public object? DefaultValue;

        public bool IsRefOrOut => Attributes.HasFlag(ParameterAttributes.Out) || RawType.byref == 1;

        public override string ToString()
        {
            var result = new StringBuilder();

            if ((Attributes & ParameterAttributes.Out) != 0)
                result.Append("out ");

            result.Append(Type).Append(" ").Append(ParameterName);

            if ((Attributes & ParameterAttributes.HasDefault) != 0)
                result.Append(" = ").Append(DefaultValue ?? "null");

            return result.ToString();
        }
    }
}