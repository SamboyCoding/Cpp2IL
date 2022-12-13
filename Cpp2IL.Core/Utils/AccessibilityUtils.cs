using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Utils;

internal static class AccessibilityUtils
{
    public static bool IsAccessibleTo(this TypeAnalysisContext referenceType, TypeAnalysisContext referencingType)
    {
        if (referenceType == referencingType)
            return true;

        var declaringTypesHierarchy = referenceType.GetTypeAndDeclaringTypes().ToArray();
        var index = declaringTypesHierarchy.IndexOf(t => referencingType.IsAssignableTo(t));

        if (referenceType.DeclaringAssembly == referencingType.DeclaringAssembly /*or internals visible*/)
        {
            for (var i = 0; i < declaringTypesHierarchy.Length; i++)
            {
                if (i == index - 1)
                {
                    if (declaringTypesHierarchy[i].GetVisibility() is TypeAttributes.NestedPrivate)
                    {
                        return false;
                    }
                }
                else
                {
                    if (declaringTypesHierarchy[i].GetVisibility() is TypeAttributes.NestedPrivate or TypeAttributes.NestedFamily or TypeAttributes.NestedFamANDAssem)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        else if (referenceType.DeclaringAssembly.Definition.IsDependencyOf(referencingType.DeclaringAssembly.Definition))
        {
            for (var i = 0; i < declaringTypesHierarchy.Length; i++)
            {
                if (i == index - 1)
                {
                    if (declaringTypesHierarchy[i].GetVisibility() is TypeAttributes.NotPublic or TypeAttributes.NestedPrivate or TypeAttributes.NestedAssembly or TypeAttributes.NestedFamANDAssem)
                    {
                        return false;
                    }
                }
                else
                {
                    if (declaringTypesHierarchy[i].GetVisibility() is TypeAttributes.NotPublic or TypeAttributes.NestedPrivate or TypeAttributes.NestedFamily or TypeAttributes.NestedAssembly or TypeAttributes.NestedFamANDAssem or TypeAttributes.NestedFamORAssem)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        return false;
    }

    private static int IndexOf<T>(this IEnumerable<T> enumerable, Func<T, bool> selector)
    {
        var index = 0;
        foreach (var item in enumerable)
        {
            if (selector(item))
            {
                return index;
            }
            index++;
        }
        return -1;
    }

    private static IEnumerable<TypeAnalysisContext> GetTypeAndDeclaringTypes(this TypeAnalysisContext type)
    {
        var current = type;
        while (current != null)
        {
            yield return current;
            current = current.DeclaringType;
        }
    }

    private static TypeAttributes GetVisibility(this TypeAnalysisContext type) => type.TypeAttributes & TypeAttributes.VisibilityMask;

    private static bool IsAssignableTo(this TypeAnalysisContext derivedType, TypeAnalysisContext baseType)
    {
        if (baseType.IsInterface)
        {
            return derivedType.IsAssignableToInterface(baseType);
        }
        else
        {
            return derivedType.InheritsFrom(baseType);
        }
    }

    private static bool InheritsFrom(this TypeAnalysisContext derivedType, TypeAnalysisContext baseType)
    {
        var current = derivedType;
        while (current != null)
        {
            if (current == baseType)
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static bool IsAssignableToInterface(this TypeAnalysisContext derivedType, TypeAnalysisContext baseInterface)
    {
        if (derivedType == baseInterface)
            return true;

        foreach (var @interface in derivedType.InterfaceContexts)
        {
            if (@interface.IsAssignableToInterface(baseInterface))
                return true;
        }

        return false;
    }

    private static bool IsDependencyOf(this Il2CppAssemblyDefinition referencedAssembly, Il2CppAssemblyDefinition referencingAssembly)
    {
        if (Array.IndexOf(referencingAssembly.ReferencedAssemblies, referencedAssembly) >= 0)
            return true;

        if (Array.IndexOf(referencedAssembly.ReferencedAssemblies, referencingAssembly) >= 0)
            return false;

        return referencingAssembly.CollectAllDependencies().Contains(referencedAssembly);
    }

    private static HashSet<Il2CppAssemblyDefinition> CollectAllDependencies(this Il2CppAssemblyDefinition referencingAssembly)
    {
        var dependencies = new HashSet<Il2CppAssemblyDefinition> { referencingAssembly };
        referencingAssembly.CollectAllDependencies(dependencies);
        return dependencies;
    }

    private static void CollectAllDependencies(this Il2CppAssemblyDefinition referencingAssembly, HashSet<Il2CppAssemblyDefinition> dependencies)
    {
        foreach (var dependency in referencingAssembly.ReferencedAssemblies)
        {
            //Assemblies can have circular references
            if (dependencies.Add(dependency))
            {
                dependency.CollectAllDependencies(dependencies);
            }
        }
    }
}
