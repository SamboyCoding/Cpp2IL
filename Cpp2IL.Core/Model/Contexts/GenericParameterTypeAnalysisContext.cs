using System;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures.Types;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class GenericParameterTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    public override string DefaultName { get; }

    public int Index { get; }

    public override Il2CppTypeEnum Type { get; }

    protected override TypeAnalysisContext ElementType => throw new("Attempted to get element type of a generic parameter");

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

    public override TypeSignature ToTypeSignature(ModuleDefinition parentModule)
    {
        return new GenericParameterSignature(Type == Il2CppTypeEnum.IL2CPP_TYPE_VAR ? GenericParameterType.Type : GenericParameterType.Method, Index);
    }
}
