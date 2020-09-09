using System.Reflection;
using System.Text;

#pragma warning disable 8618
namespace LibCpp2IL.Reflection
{
    public class Il2CppParameterReflectionData
    {
        public string ParameterName;
        public Il2CppTypeReflectionData Type;
        public ParameterAttributes ParameterAttributes;
        public object? DefaultValue;

        public override string ToString()
        {
            var result = new StringBuilder();

            if ((ParameterAttributes & ParameterAttributes.Out) != 0)
                result.Append("out ");

            result.Append(Type).Append(" ").Append(ParameterName);

            if (DefaultValue != null)
                result.Append(" = ").Append(DefaultValue);

            return result.ToString();
        }
    }
}