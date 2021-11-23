using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;

namespace Cpp2IL.Core.Utils
{
    public static class GenericMethodUtils
    {
        public static MethodReference PrepareGenericMethodForEmissionToBody(MethodReference theMethod, TypeReference instanceType, MethodDefinition contextMethod, ModuleDefinition importInto)
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

            TypeReference CleanReturnType = null;
            TypeReference CleanDeclaringType = null;
            MethodReference CleanMethodRef = null;
            List<TypeReference> CleanParameterTypes = null;

            if (!CecilUtils.HasAnyGenericCrapAnywhere(theMethod))
                //Method is clean, we're ok
                return theMethod;

            if (!CecilUtils.HasAnyGenericCrapAnywhere(theMethod.ReturnType))
                CleanReturnType = theMethod.ReturnType;
            else
            {
                //Attempt to resolve generics in return type from method or declaring type
            }

            if (!CecilUtils.HasAnyGenericCrapAnywhere(theMethod.DeclaringType))
                CleanDeclaringType = theMethod.DeclaringType;
            else
            {
                //Attempt to resolve generics in declaring type from context (e.g. parameters, contextual method and/or type, etc)
            }

            if (!CecilUtils.HasAnyGenericCrapAnywhere(theMethod, false))
                CleanMethodRef = theMethod; //Can only take generic params and actual method name from this, we can't guarantee anything else
            else
            {
                //Attempt to resolve method generic params from context (most likely contextual method and/or type, could be arguments)
            }

            if (theMethod.Parameters.All(p => !CecilUtils.HasAnyGenericCrapAnywhere(p.ParameterType)))
                CleanParameterTypes = theMethod.Parameters.Select(p => p.ParameterType).ToList();
            else
            {
                //Attempt to resolve generics in params against method and/or type. Note this means setting the type to the generic parameter where it's defined, not replacing it with the actual type.
                //Also note that this is actually set up correctly, it just can't be used with ImportReference.
            }

            var ret = new MethodReference(CleanMethodRef.Name, CleanReturnType, CleanDeclaringType);
            if (CleanMethodRef is GenericInstanceMethod gim)
            {
                var newGim = new GenericInstanceMethod(ret);
                foreach (var gimGenericArgument in gim.GenericArguments)
                {
                    newGim.GenericArguments.Add(gimGenericArgument);
                }

                ret = newGim;
            } else if (theMethod.HasGenericParameters)
            {
                //Failed to resolve all generic params
                throw new($"Failed to resolve all generic method params in PrepareGenericMethodForEmissionToBody. Original method was {theMethod}, on instance type {instanceType}, with context {contextMethod}");
            }

            for (var i = 0; i < theMethod.Parameters.Count; i++)
            {
                var origParam = theMethod.Parameters[i];
                ret.Parameters.Add(new(origParam.Name, origParam.Attributes, CleanParameterTypes[i]));
            }

            return ret;
        }
    }
}