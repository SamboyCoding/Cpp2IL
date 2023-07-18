using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class GenericParameterTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    protected override TypeAnalysisContext ElementType => throw new("Attempted to get element type of a generic parameter");

    public GenericParameterTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom) : base(rawType, referencedFrom)
    {
        if(rawType.Type is not Il2CppTypeEnum.IL2CPP_TYPE_VAR and not Il2CppTypeEnum.IL2CPP_TYPE_MVAR)
            throw new($"Generic parameter type is not a generic parameter, but {rawType.Type}");

        GenericParameter = rawType.GetGenericParameterDef();
    }
}
