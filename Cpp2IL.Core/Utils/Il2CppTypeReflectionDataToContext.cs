using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Utils;

public static class Il2CppTypeReflectionDataToContext
{
    public static TypeAnalysisContext? ToContext(this Il2CppTypeReflectionData reflectionData, AssemblyAnalysisContext assembly, int genericParamIndex = -1)
    {
        TypeAnalysisContext? pointerElementType;

        if (reflectionData.isArray)
        {
            var arrayElementType = reflectionData.arrayType?.ToContext(assembly);
            if (arrayElementType is null)
            {
                return null;
            }
            pointerElementType = new ArrayTypeAnalysisContext(arrayElementType, reflectionData.arrayRank, assembly);
        }
        else if (!reflectionData.isType)
        {
            pointerElementType = new GenericParameterTypeAnalysisContext(reflectionData.variableGenericParamName, genericParamIndex, Il2CppTypeEnum.IL2CPP_TYPE_VAR, assembly);
        }
        else if (!reflectionData.isGenericType)
        {
            pointerElementType = reflectionData.baseType is null ? null : assembly.AppContext.ResolveContextForType(reflectionData.baseType);
        }
        else
        {
            var baseType = reflectionData.baseType is null ? null : assembly.AppContext.ResolveContextForType(reflectionData.baseType);
            if (baseType == null)
            {
                return null;
            }
            var genericParams = new TypeAnalysisContext[reflectionData.genericParams.Length];
            for (var i = 0; i < reflectionData.genericParams.Length; i++)
            {
                var param = reflectionData.genericParams[i].ToContext(assembly, i);
                if (param == null)
                {
                    return null;
                }
                genericParams[i] = param;
            }
            pointerElementType = new GenericInstanceTypeAnalysisContext(baseType, genericParams, assembly);
        }

        if (reflectionData.isPointer && pointerElementType is not null)
        {
            return new PointerTypeAnalysisContext(pointerElementType, assembly);
        }
        else
        {
            return pointerElementType;
        }
    }
}
