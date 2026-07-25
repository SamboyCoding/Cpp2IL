using System.Linq;
using System.Text;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace LibCpp2IL;

public class Cpp2IlMethodRef(Il2CppMethodSpec methodSpec)
{
    public Il2CppTypeDefinition DeclaringType => BaseMethod.DeclaringType!;
    public Il2CppType[] TypeGenericParams => methodSpec.GenericClassParams;
    public Il2CppMethodDefinition BaseMethod => methodSpec.MethodDefinition!;
    public Il2CppType[] MethodGenericParams => methodSpec.GenericMethodParams;

    public ulong GenericVariantPtr;

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append(BaseMethod.ReturnType).Append(" ");

        sb.Append(DeclaringType.FullName);

        if (TypeGenericParams.Length > 0)
            sb.Append("<").Append(string.Join(", ", TypeGenericParams.AsEnumerable())).Append(">");

        sb.Append(".").Append(BaseMethod.Name);

        if (MethodGenericParams.Length > 0)
            sb.Append("<").Append(string.Join(", ", MethodGenericParams.AsEnumerable())).Append(">");

        return sb.ToString();
    }
}
