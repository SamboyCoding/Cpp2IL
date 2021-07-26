using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class GenericMethodReference
    {
        public TypeReference Type;
        public MethodReference Method;

        public GenericMethodReference(TypeReference type, MethodReference method)
        {
            Type = type;
            Method = method;
        }

        public override string ToString()
        {
            return $"generic method reference for method {Method} on type {Type}";
        }
    }
}