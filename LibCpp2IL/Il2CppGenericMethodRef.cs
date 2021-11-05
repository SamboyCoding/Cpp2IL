using System.Linq;
using System.Text;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
#pragma warning disable 8618

namespace LibCpp2IL
{
    public class Il2CppGenericMethodRef
    {
        public readonly Il2CppTypeDefinition DeclaringType;
        public readonly Il2CppTypeReflectionData[] TypeGenericParams;
        public readonly Il2CppMethodDefinition BaseMethod;
        public readonly Il2CppTypeReflectionData[] MethodGenericParams;

        public ulong GenericVariantPtr;

        public Il2CppGenericMethodRef(Il2CppMethodSpec methodSpec)
        {
            var typeName = methodSpec.MethodDefinition!.DeclaringType!.FullName;

            Il2CppTypeReflectionData[] declaringTypeGenericParams = new Il2CppTypeReflectionData[0];
            if (methodSpec.classIndexIndex != -1)
            {
                var classInst = methodSpec.GenericClassInst;
                declaringTypeGenericParams = LibCpp2ILUtils.GetGenericTypeParams(classInst!)!;
                typeName += LibCpp2ILUtils.GetGenericTypeParamNames(LibCpp2IlMain.TheMetadata!, LibCpp2IlMain.Binary!,
                    classInst!);
            }

            var methodName = typeName + "." + methodSpec.MethodDefinition.Name;

            Il2CppTypeReflectionData[] genericMethodParameters = new Il2CppTypeReflectionData[0];
            if (methodSpec.methodIndexIndex != -1)
            {
                var methodInst = methodSpec.GenericMethodInst;
                methodName +=
                    LibCpp2ILUtils.GetGenericTypeParamNames(LibCpp2IlMain.TheMetadata!, LibCpp2IlMain.Binary!, methodInst!);
                genericMethodParameters = LibCpp2ILUtils.GetGenericTypeParams(methodInst!)!;
            }

            BaseMethod = methodSpec.MethodDefinition;
            DeclaringType = methodSpec.MethodDefinition.DeclaringType;
            TypeGenericParams = declaringTypeGenericParams;
            MethodGenericParams = genericMethodParameters;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.Append(BaseMethod?.ReturnType).Append(" ");

            sb.Append(DeclaringType.FullName);

            if (TypeGenericParams.Length > 0)
                sb.Append("<").Append(string.Join(", ", TypeGenericParams.AsEnumerable())).Append(">");

            sb.Append(".").Append(BaseMethod?.Name);
            
            if(MethodGenericParams.Length > 0)
                sb.Append("<").Append(string.Join(", ", MethodGenericParams.AsEnumerable())).Append(">");

            return sb.ToString();
        }
    }
}