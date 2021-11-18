using System.Diagnostics;
using Mono.Cecil;

namespace Cpp2IL.Core.Utils
{
    public static class GenericMethodUtils
    {
        public static void PrepareGenericMethodForEmissionToBody(MethodReference theMethod, TypeReference declaringType, ModuleDefinition importInto)
        {
            //Naively resolving everything and its mother doesn't work.
            //There are four key parts here. From left-to-right in the signature
            //    The return type, including generic arguments
            //    The declaring type, including generic arguments
            //    The method specification itself, including any generic arguments
            //    The parameters, with any generic parameters in the source method definition resolved to match those of method reference.
            
            //The key part is that generic *arguments* on the parameters and return type of the resulting method reference have to be the generic *parameters* of the method definition or type.
            //E.g. Enumerable.Where<T>(IEnumerable<T> source, Func<T, bool> predicate), the 'T's in the parameters need to resolve to the T in the method Where, even if the method call itself
            //has a generic argument.
            //So this should look like Enumerable.Where<int>(IEnumerable<T> source, Func<T, bool> predicate) still, and NOT have the T in the parameters replaced with int.
            
            //And every single individual part has to be imported.
            //And we have to return a unique new MethodReference for this call specifically.
            
            if(theMethod is MethodDefinition && declaringType is TypeDefinition)
                return;
            
            //Debugger.Break();
        }
    }
}