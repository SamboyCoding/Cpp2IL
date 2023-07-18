using System;
using System.Collections.Generic;
using System.Linq;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents any kind of type context that is not a basic type definition. This includes generic instantiations, byref/pointer types, arrays, etc.
/// </summary>
public abstract class ReferencedTypeAnalysisContext : TypeAnalysisContext
{
    public Il2CppType RawType { get; }
    
    protected abstract TypeAnalysisContext ElementType { get; } //Must be set by derived classes

    protected List<TypeAnalysisContext> GenericArguments { get; } = new();
    
    protected Il2CppGenericParameter? GenericParameter { get; set; }

    public override string DefaultNs => ElementType.Namespace;

    public override string DefaultName => RawType.Type switch
    {
        Il2CppTypeEnum.IL2CPP_TYPE_PTR => $"{ElementType.Name}*",
        Il2CppTypeEnum.IL2CPP_TYPE_BYREF => $"{ElementType.Name}&",
        Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY => $"{ElementType.Name}[]",
        Il2CppTypeEnum.IL2CPP_TYPE_ARRAY => $"{ElementType.Name}[{RawType.GetArrayRank()}]",
        Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST => $"{ElementType.Name}<{string.Join(", ", GenericArguments.Select(a => a.Name))}>",
        Il2CppTypeEnum.IL2CPP_TYPE_VAR => GenericParameter!.Name!,
        Il2CppTypeEnum.IL2CPP_TYPE_MVAR => GenericParameter!.Name!,
        _ => throw new ArgumentOutOfRangeException(),
    };

    protected override int CustomAttributeIndex => -1;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringAssembly;

    protected ReferencedTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom) : base(null, referencedFrom)
    {
        RawType = rawType;
    }

    public override string ToString()
    {
        return $"{DefaultName}";
    }

    public override string GetCSharpSourceString()
    {
        return Name;
    }
}
