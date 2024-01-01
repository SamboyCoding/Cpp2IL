using System;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class GenericParameterTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    public sealed override string DefaultName { get; }

    public sealed override string DefaultNs => "";

    public int Index { get; }

    public override Il2CppTypeEnum Type { get; }

    public GenericParameterTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom)
        : this(rawType.GetGenericParameterDef(), rawType.Type, referencedFrom)
    {
    }

    public GenericParameterTypeAnalysisContext(Il2CppGenericParameter genericParameter, Il2CppTypeEnum type, AssemblyAnalysisContext referencedFrom)
        : this(genericParameter.Name ?? "T", genericParameter.genericParameterIndexInOwner, type, referencedFrom)
    {
    }

    public GenericParameterTypeAnalysisContext(string name, int index, Il2CppTypeEnum type, AssemblyAnalysisContext referencedFrom) : base(referencedFrom)
    {
        if (type is not Il2CppTypeEnum.IL2CPP_TYPE_VAR and not Il2CppTypeEnum.IL2CPP_TYPE_MVAR)
            throw new ArgumentException($"Generic parameter type is not a generic parameter, but {type}", nameof(type));

        DefaultName = name;
        Index = index;
        Type = type;
    }
}
