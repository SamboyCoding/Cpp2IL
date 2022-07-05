using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class GenericParameterTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    public GenericParameterTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom) : base(rawType, referencedFrom)
    {
        if(rawType.Type is not Il2CppTypeEnum.IL2CPP_TYPE_VAR and not Il2CppTypeEnum.IL2CPP_TYPE_MVAR)
            throw new($"Generic parameter type is not a generic parameter, but {rawType.Type}");

        GenericParameter = rawType.GetGenericParameterDef();
    }
}