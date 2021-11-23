using System.Linq;
using Mono.Cecil;

namespace Cpp2IL.Core.Utils
{
    public static class CecilUtils
    {
        public static bool HasAnyGenericCrapAnywhere(TypeReference reference)
        {
            if (reference is GenericParameter)
                return true;
            
            if (reference is GenericInstanceType git)
            {
                //check for e.g. List<List<T>> 
                return git.GenericArguments.Any(HasAnyGenericCrapAnywhere);
            }

            if (reference is TypeSpecification typeSpec)
                //Pointers, byrefs, etc
                return HasAnyGenericCrapAnywhere(typeSpec.ElementType);

            return reference.HasGenericParameters;
        }

        public static bool HasAnyGenericCrapAnywhere(MethodReference reference, bool checkDeclaringTypeParamsAndReturn = true)
        {
            if (checkDeclaringTypeParamsAndReturn && HasAnyGenericCrapAnywhere(reference.DeclaringType))
                return true;

            if (checkDeclaringTypeParamsAndReturn && HasAnyGenericCrapAnywhere(reference.ReturnType))
                return true;

            if (checkDeclaringTypeParamsAndReturn && reference.Parameters.Any(p => HasAnyGenericCrapAnywhere(p.ParameterType)))
                return true;

            if (reference.HasGenericParameters)
                return true;

            if (reference is GenericInstanceMethod gim)
                return gim.GenericArguments.Any(HasAnyGenericCrapAnywhere);

            return false;
        }
    }
}