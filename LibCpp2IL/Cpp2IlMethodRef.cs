using System.Linq;
using System.Text;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL;

public class Cpp2IlMethodRef(Il2CppMethodSpec methodSpec)
{
    public Il2CppTypeDefinition DeclaringType => BaseMethod.DeclaringType!;
    public Il2CppTypeReflectionData[] TypeGenericParams => methodSpec.GenericClassParams;
    public Il2CppMethodDefinition BaseMethod => methodSpec.MethodDefinition!;
    public Il2CppTypeReflectionData[] MethodGenericParams => methodSpec.GenericMethodParams;

    public ulong GenericVariantPtr;

    // var declaringTypeGenericParams = Array.Empty<Il2CppTypeReflectionData>();
    // if (methodSpec.classIndexIndex != -1)
    // {
    //     var classInst = methodSpec.GenericClassInst;
    //     declaringTypeGenericParams = LibCpp2ILUtils.GetGenericTypeParams(classInst!)!;
    // }
    //
    // var genericMethodParameters = Array.Empty<Il2CppTypeReflectionData>();
    // if (methodSpec.methodIndexIndex != -1)
    // {
    //     var methodInst = methodSpec.GenericMethodInst;
    //     genericMethodParameters = LibCpp2ILUtils.GetGenericTypeParams(methodInst!)!;
    // }
    //
    // BaseMethod = methodSpec.MethodDefinition!;
    // DeclaringType = methodSpec.MethodDefinition!.DeclaringType!;
    // TypeGenericParams = declaringTypeGenericParams;
    // MethodGenericParams = genericMethodParameters;

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
