using System;
using System.Linq;
using System.Text;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
#pragma warning disable 8618

namespace LibCpp2IL
{
    public class Cpp2IlMethodRef
    {
        public readonly Il2CppTypeDefinition DeclaringType;
        public readonly Il2CppTypeReflectionData[] TypeGenericParams;
        public readonly Il2CppMethodDefinition BaseMethod;
        public readonly Il2CppTypeReflectionData[] MethodGenericParams;

        public ulong GenericVariantPtr;

        public Cpp2IlMethodRef(Il2CppMethodSpec methodSpec)
        {
            var declaringTypeGenericParams = Array.Empty<Il2CppTypeReflectionData>();
            if (methodSpec.classIndexIndex != -1)
            {
                var classInst = methodSpec.GenericClassInst;
                declaringTypeGenericParams = LibCpp2ILUtils.GetGenericTypeParams(classInst!)!;
            }

            var genericMethodParameters = Array.Empty<Il2CppTypeReflectionData>();
            if (methodSpec.methodIndexIndex != -1)
            {
                var methodInst = methodSpec.GenericMethodInst;
                genericMethodParameters = LibCpp2ILUtils.GetGenericTypeParams(methodInst!)!;
            }

            BaseMethod = methodSpec.MethodDefinition!;
            DeclaringType = methodSpec.MethodDefinition!.DeclaringType!;
            TypeGenericParams = declaringTypeGenericParams;
            MethodGenericParams = genericMethodParameters;
        }

        public Cpp2IlMethodRef(Il2CppMethodDefinition methodDefinition)
        {
            BaseMethod = methodDefinition;
            DeclaringType = methodDefinition.DeclaringType!;
            TypeGenericParams = Array.Empty<Il2CppTypeReflectionData>();
            MethodGenericParams = Array.Empty<Il2CppTypeReflectionData>();
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