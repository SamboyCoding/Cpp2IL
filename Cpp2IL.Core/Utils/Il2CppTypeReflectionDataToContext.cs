using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Utils;

public static class Il2CppTypeReflectionDataToContext
{
    public static TypeAnalysisContext? ToContext(this Il2CppTypeReflectionData reflectionData, ApplicationAnalysisContext appContext)
    {
        TypeAnalysisContext? pointerElementType;

        if (reflectionData.isArray)
        {
            var arrayElementType = reflectionData.arrayType?.ToContext(appContext);
            if (arrayElementType is null)
            {
                return null;
            }

            pointerElementType = new ArrayTypeAnalysisContext(arrayElementType, reflectionData.arrayRank);
        }
        else if (!reflectionData.isType)
        {
            pointerElementType = appContext.ResolveContextForGenericParameter(reflectionData.GenericParameter);
        }
        else if (!reflectionData.isGenericType)
        {
            pointerElementType = reflectionData.baseType is null ? null : appContext.ResolveContextForType(reflectionData.baseType);
        }
        else
        {
            var baseType = reflectionData.baseType is null ? null : appContext.ResolveContextForType(reflectionData.baseType);
            if (baseType == null)
            {
                return null;
            }

            var genericParams = new TypeAnalysisContext[reflectionData.genericParams.Length];
            for (var i = 0; i < reflectionData.genericParams.Length; i++)
            {
                var param = reflectionData.genericParams[i].ToContext(appContext);
                if (param == null)
                {
                    return null;
                }

                genericParams[i] = param;
            }

            pointerElementType = new GenericInstanceTypeAnalysisContext(baseType, genericParams);
        }

        if (reflectionData.isPointer && pointerElementType is not null)
        {
            return new PointerTypeAnalysisContext(pointerElementType);
        }
        else
        {
            return pointerElementType;
        }
    }
}
