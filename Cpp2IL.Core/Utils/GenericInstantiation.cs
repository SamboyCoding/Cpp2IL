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
                    : new SzArrayTypeAnalysisContext(elementType);
            }
            case ArrayTypeAnalysisContext arrayTypeAnalysisContext:
            {
                var elementType = Instantiate(arrayTypeAnalysisContext.ElementType, genericTypeParameters, genericMethodParameters);
                return elementType == arrayTypeAnalysisContext.ElementType
                    ? arrayTypeAnalysisContext
                    : new ArrayTypeAnalysisContext(elementType, arrayTypeAnalysisContext.Rank);
            }
            case ByRefTypeAnalysisContext byReferenceTypeAnalysisContext:
            {
                var elementType = Instantiate(byReferenceTypeAnalysisContext.ElementType, genericTypeParameters, genericMethodParameters);
                return elementType == byReferenceTypeAnalysisContext.ElementType
                    ? byReferenceTypeAnalysisContext
                    : new ByRefTypeAnalysisContext(elementType);
            }
            case PointerTypeAnalysisContext pointerTypeAnalysisContext:
            {
                var elementType = Instantiate(pointerTypeAnalysisContext.ElementType, genericTypeParameters, genericMethodParameters);
                return elementType == pointerTypeAnalysisContext.ElementType
                    ? pointerTypeAnalysisContext
                    : new PointerTypeAnalysisContext(elementType);
            }
            case PinnedTypeAnalysisContext pinnedTypeAnalysisContext:
            {
                var elementType = Instantiate(pinnedTypeAnalysisContext.ElementType, genericTypeParameters, genericMethodParameters);
                return elementType == pinnedTypeAnalysisContext.ElementType
                    ? pinnedTypeAnalysisContext
                    : new PinnedTypeAnalysisContext(elementType);
            }
            case BoxedTypeAnalysisContext boxedTypeAnalysisContext:
            {
                var elementType = Instantiate(boxedTypeAnalysisContext.ElementType, genericTypeParameters, genericMethodParameters);
                return elementType == boxedTypeAnalysisContext.ElementType
                    ? boxedTypeAnalysisContext
                    : new BoxedTypeAnalysisContext(elementType);
            }
            case CustomModifierTypeAnalysisContext customModifierTypeAnalysisContext:
            {
                var elementType = Instantiate(customModifierTypeAnalysisContext.ElementType, genericTypeParameters, genericMethodParameters);
                var modifierType = Instantiate(customModifierTypeAnalysisContext.ModifierType, genericTypeParameters, genericMethodParameters);
                return (elementType == customModifierTypeAnalysisContext.ElementType && modifierType == customModifierTypeAnalysisContext.ModifierType)
                    ? customModifierTypeAnalysisContext
                    : new CustomModifierTypeAnalysisContext(elementType, modifierType, customModifierTypeAnalysisContext.Required);
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
                    ? new GenericInstanceTypeAnalysisContext(genericType, genericArguments)
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
        PinnedTypeAnalysisContext pinnedTypeAnalysisContext => HasAnyGenericParameters(pinnedTypeAnalysisContext.ElementType),
        BoxedTypeAnalysisContext boxedTypeAnalysisContext => HasAnyGenericParameters(boxedTypeAnalysisContext.ElementType),
        CustomModifierTypeAnalysisContext customModifierTypeAnalysisContext => HasAnyGenericParameters(customModifierTypeAnalysisContext.ElementType) || HasAnyGenericParameters(customModifierTypeAnalysisContext.ModifierType),
        GenericInstanceTypeAnalysisContext genericInstanceTypeAnalysisContext => genericInstanceTypeAnalysisContext.GenericArguments.Any(HasAnyGenericParameters),
        _ => false,
    };
}
