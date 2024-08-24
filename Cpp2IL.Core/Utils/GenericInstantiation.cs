using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Utils;

internal static class GenericInstantiation
{
    public static TypeAnalysisContext Instantiate(TypeAnalysisContext type, IReadOnlyList<TypeAnalysisContext> genericTypeParameters, IReadOnlyList<TypeAnalysisContext> genericMethodParameters)
    {
        switch (type)
        {
            case GenericParameterTypeAnalysisContext genericParameterTypeAnalysisContext:
            {
                var index = genericParameterTypeAnalysisContext.Index;
                return genericParameterTypeAnalysisContext.Type switch
                {
                    Il2CppTypeEnum.IL2CPP_TYPE_VAR => genericTypeParameters[index],
                    _ => genericMethodParameters[index],
                };
            }
            case SzArrayTypeAnalysisContext szArrayTypeAnalysisContext:
            {
                var elementType = Instantiate(szArrayTypeAnalysisContext.ElementType, genericTypeParameters, genericMethodParameters);
                return elementType == szArrayTypeAnalysisContext.ElementType
                    ? szArrayTypeAnalysisContext
                    : new SzArrayTypeAnalysisContext(elementType, szArrayTypeAnalysisContext.DeclaringAssembly);
            }
            case ArrayTypeAnalysisContext arrayTypeAnalysisContext:
            {
                var elementType = Instantiate(arrayTypeAnalysisContext.ElementType, genericTypeParameters, genericMethodParameters);
                return elementType == arrayTypeAnalysisContext.ElementType
                    ? arrayTypeAnalysisContext
                    : new ArrayTypeAnalysisContext(elementType, arrayTypeAnalysisContext.Rank, arrayTypeAnalysisContext.DeclaringAssembly);
            }
            case ByRefTypeAnalysisContext byReferenceTypeAnalysisContext:
            {
                var elementType = Instantiate(byReferenceTypeAnalysisContext.ElementType, genericTypeParameters, genericMethodParameters);
                return elementType == byReferenceTypeAnalysisContext.ElementType
                    ? byReferenceTypeAnalysisContext
                    : new ByRefTypeAnalysisContext(elementType, byReferenceTypeAnalysisContext.DeclaringAssembly);
            }
            case PointerTypeAnalysisContext pointerTypeAnalysisContext:
            {
                var elementType = Instantiate(pointerTypeAnalysisContext.ElementType, genericTypeParameters, genericMethodParameters);
                return elementType == pointerTypeAnalysisContext.ElementType
                    ? pointerTypeAnalysisContext
                    : new PointerTypeAnalysisContext(elementType, pointerTypeAnalysisContext.DeclaringAssembly);
            }
            case GenericInstanceTypeAnalysisContext genericInstanceTypeAnalysisContext:
            {
                var genericType = Instantiate(genericInstanceTypeAnalysisContext.GenericType, genericTypeParameters, genericMethodParameters);

                var createNew = genericType != genericInstanceTypeAnalysisContext.GenericType;

                var genericArguments = new TypeAnalysisContext[genericInstanceTypeAnalysisContext.GenericArguments.Count];
                for (var i = 0; i < genericInstanceTypeAnalysisContext.GenericArguments.Count; i++)
                {
                    var genericArgument = genericInstanceTypeAnalysisContext.GenericArguments[i];
                    var instantiatedGenericArgument = Instantiate(genericArgument, genericTypeParameters, genericMethodParameters);
                    genericArguments[i] = instantiatedGenericArgument;
                    createNew |= instantiatedGenericArgument != genericArgument;
                }

                return createNew
                    ? new GenericInstanceTypeAnalysisContext(genericType, genericArguments, genericInstanceTypeAnalysisContext.DeclaringAssembly)
                    : genericInstanceTypeAnalysisContext;
            }
            default:
                return type;
        }
    }

    public static bool HasAnyGenericParameters(this TypeAnalysisContext type) => type switch
    {
        GenericParameterTypeAnalysisContext => true,
        SzArrayTypeAnalysisContext szArrayTypeAnalysisContext => HasAnyGenericParameters(szArrayTypeAnalysisContext.ElementType),
        ArrayTypeAnalysisContext arrayTypeAnalysisContext => HasAnyGenericParameters(arrayTypeAnalysisContext.ElementType),
        ByRefTypeAnalysisContext byReferenceTypeAnalysisContext => HasAnyGenericParameters(byReferenceTypeAnalysisContext.ElementType),
        PointerTypeAnalysisContext pointerTypeAnalysisContext => HasAnyGenericParameters(pointerTypeAnalysisContext.ElementType),
        GenericInstanceTypeAnalysisContext genericInstanceTypeAnalysisContext => genericInstanceTypeAnalysisContext.GenericArguments.Any(HasAnyGenericParameters),
        _ => false,
    };
}
