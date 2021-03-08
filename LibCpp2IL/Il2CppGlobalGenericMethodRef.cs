using System.Linq;
using System.Text;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
#pragma warning disable 8618

namespace LibCpp2IL
{
    public class Il2CppGlobalGenericMethodRef
    {
        public Il2CppTypeDefinition declaringType;
        public Il2CppTypeReflectionData[] typeGenericParams;
        public Il2CppMethodDefinition baseMethod;
        public Il2CppTypeReflectionData[] methodGenericParams;

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.Append(baseMethod?.ReturnType).Append(" ");

            sb.Append(declaringType.FullName);

            if (typeGenericParams.Length > 0)
                sb.Append("<").Append(string.Join(", ", typeGenericParams.AsEnumerable())).Append(">");

            sb.Append(".").Append(baseMethod?.Name);
            
            if(methodGenericParams.Length > 0)
                sb.Append("<").Append(string.Join(", ", methodGenericParams.GetEnumerator())).Append(">");

            return sb.ToString();
        }
    }
}