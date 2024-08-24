using System.Reflection;
using System.Text;
using LibCpp2IL.BinaryStructures;

#pragma warning disable 8618
namespace LibCpp2IL.Reflection;

public class Il2CppParameterReflectionData
{
    public string ParameterName;
    public Il2CppType RawType;
    public Il2CppTypeReflectionData Type;
    public ParameterAttributes Attributes;
    public object? DefaultValue;
    public int ParameterIndex;

    public bool IsRefOrOut => Attributes.HasFlag(ParameterAttributes.Out) || RawType.Byref == 1;

    public override string ToString()
    {
        var result = new StringBuilder();

        if (Attributes.HasFlag(ParameterAttributes.Out))
            result.Append("out ");
        else if (Attributes.HasFlag(ParameterAttributes.In))
            result.Append("in ");
        else if (RawType.Byref == 1)
            result.Append("ref ");

        result.Append(Type).Append(" ");

        if (string.IsNullOrEmpty(ParameterName))
            result.Append("param_").Append(ParameterIndex);
        else
            result.Append(ParameterName);

        if (Attributes.HasFlag(ParameterAttributes.HasDefault))
            result.Append(" = ").Append(DefaultValue ?? "null");

        return result.ToString();
    }
}
