using System.Linq;
using System.Text;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.PE
{
    public class Il2CppMethodSpec
    {
        public int methodDefinitionIndex;
        public int classIndexIndex;
        public int methodIndexIndex;

        public Il2CppMethodDefinition? MethodDefinition => LibCpp2IlMain.TheMetadata?.methodDefs[methodDefinitionIndex];

        public Il2CppGenericInst? GenericClassInst => LibCpp2IlMain.ThePe?.GetGenericInst(classIndexIndex);
        
        public Il2CppGenericInst? GenericMethodInst => LibCpp2IlMain.ThePe?.GetGenericInst(methodIndexIndex);

        public Il2CppTypeReflectionData[] GenericClassParams => LibCpp2ILUtils.GetGenericTypeParams(GenericClassInst!)!;
        
        public Il2CppTypeReflectionData[] GenericMethodParams => LibCpp2ILUtils.GetGenericTypeParams(GenericMethodInst!)!;

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.Append(MethodDefinition?.ReturnType).Append(" ");

            sb.Append(MethodDefinition?.DeclaringType?.FullName);

            if (classIndexIndex != -1)
                sb.Append("<").Append(string.Join(", ", GenericClassParams.AsEnumerable())).Append(">");

            sb.Append(".").Append(MethodDefinition?.Name);
            
            if(methodIndexIndex != -1)
                sb.Append("<").Append(string.Join(", ", GenericMethodParams.GetEnumerator())).Append(">");

            return sb.ToString();
        }
    };
}