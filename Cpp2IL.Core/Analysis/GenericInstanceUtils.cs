using System;
using System.Linq;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Cpp2IL.Core.Analysis
{
    public static class GenericInstanceUtils
    {
        private static int GetIndexOfGenericParameterWithName(GenericInstanceType git, string name)
        {
            var res = git.ElementType.Resolve().GenericParameters.ToList().FindIndex(g => g.Name == name);
            if (res >= 0)
                return res;

            return git.GenericArguments.ToList().FindIndex(g => g.Name == name);
        }

        internal static TypeReference? GetGenericArgumentByNameFromGenericInstanceType(GenericInstanceType git, GenericParameter gp)
        {
            if (GetIndexOfGenericParameterWithName(git, gp.FullName) is { } genericIdx && genericIdx >= 0)
                return git.GenericArguments[genericIdx];

            return null;
        }

        internal static TypeReference? ResolveGenericParameterType(GenericParameter gp, TypeReference? instance, MethodReference? method = null)
        {
            var git = instance as GenericInstanceType ?? method?.DeclaringType as GenericInstanceType;

            if (git != null && GetGenericArgumentByNameFromGenericInstanceType(git, gp) is { } t)
                return t;

            if (method is GenericInstanceMethod gim && gim.ElementMethod.HasGenericParameters)
            {
                var p = gim.ElementMethod.GenericParameters.ToList();

                if (git != null && git.ElementType.HasGenericParameters)
                    //Filter to generic params not specified in the type
                    p = p.Where(genericParam => git.ElementType.GenericParameters.All(gitP => gitP.FullName != genericParam.FullName)).ToList();

                if (p.FindIndex(g => g.Name == gp.FullName) is { } methodGenericIdx && methodGenericIdx >= 0)
                    return gim.GenericArguments[methodGenericIdx];
            }

            if (instance?.GenericParameters.FirstOrDefault(gp2 => gp2.Name == gp.Name) is { } matchingGpInInstance && matchingGpInInstance != gp)
                return matchingGpInInstance;

            if (instance?.Resolve()?.BaseType is { } bt)
                return ResolveGenericParameterType(gp, bt, method);

            return null;
        }

        private static TypeReference? TryLookupGenericParamBasedOnFunctionArguments(GenericParameter p, MethodReference methodReference, TypeReference?[] argumentTypes)
        {
            if (!methodReference.HasGenericParameters) return null;

            for (var i = 0; i < methodReference.Parameters.Count; i++)
            {
                var parameterType = methodReference.Parameters[i].ParameterType;

                if (argumentTypes.Length <= i)
                    return null;

                var argumentType = argumentTypes[i];

                if (parameterType.IsGenericInstance && parameterType is GenericInstanceType baseGit && argumentType is GenericInstanceType actualGit && GetIndexOfGenericParameterWithName(baseGit, p.FullName) is { } idx && idx >= 0)
                {
                    return actualGit.GenericArguments[idx];
                }

                if (parameterType.IsGenericParameter && parameterType.FullName == p.FullName)
                    return argumentType;
            }

            return null;
        }

        internal static GenericInstanceType ResolveMethodGIT(GenericInstanceType unresolved, MethodReference method, TypeReference? instance, TypeReference?[] parameterTypes)
        {
            var baseType = unresolved.ElementType;

            var genericArgs = unresolved.GenericArguments.Select(
                ga => !(ga is GenericParameter p) ? ga : ResolveGenericParameterType(p, instance, method) ?? TryLookupGenericParamBasedOnFunctionArguments(p, method, parameterTypes)
            ).ToArray();

            if (genericArgs.Any(g => g == null))
                throw new Exception($"Generic argument null! Full list {genericArgs.ToStringEnumerable()} (length {genericArgs.Length}, nulls omitted), base type {unresolved}");

            return baseType.MakeGenericInstanceType(genericArgs);
        }
    }
}