using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class GenericParameterTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    private readonly Il2CppGenericParameter? definition;

    public sealed override string DefaultName { get; }

    public sealed override string DefaultNamespace => "";

    public sealed override string? OverrideNamespace
    {
        get => null;
        set
        {
        }
    }

    public int Index { get; }

    public sealed override Il2CppTypeEnum Type { get; }

    public new GenericParameterAttributes DefaultAttributes { get; }
    public new GenericParameterAttributes? OverrideAttributes { get; set; }
    public new GenericParameterAttributes Attributes
    {
        get => OverrideAttributes ?? DefaultAttributes;
        set => OverrideAttributes = value;
    }

    private List<TypeAnalysisContext>? _constraintTypes;
    public List<TypeAnalysisContext> ConstraintTypes
    {
        get
        {
            _constraintTypes ??= definition?.ConstraintTypes.Select(t => AppContext.ResolveIl2CppType(t)).ToList() ?? [];
            return _constraintTypes;
        }
    }

    public HasGenericParameters Owner { get; }

    /// <summary>
    /// This should only be used by the initializers for <see cref="TypeAnalysisContext.GenericParameters"/> and <see cref="MethodAnalysisContext.GenericParameters"/>.
    /// It ensures that generic parameters are only held by their owners.
    /// </summary>
    internal GenericParameterTypeAnalysisContext(Il2CppGenericParameter genericParameter, HasGenericParameters owner)
        : this(genericParameter.Name ?? "T", genericParameter.genericParameterIndexInOwner, genericParameter.Type, genericParameter.Attributes, owner)
    {
        definition = genericParameter;
    }

    public GenericParameterTypeAnalysisContext(string name, int index, Il2CppTypeEnum type, GenericParameterAttributes attributes, HasGenericParameters owner) : base(owner.CustomAttributeAssembly)
    {
        if (type is not Il2CppTypeEnum.IL2CPP_TYPE_VAR and not Il2CppTypeEnum.IL2CPP_TYPE_MVAR)
            throw new ArgumentException($"Generic parameter type is not a generic parameter, but {type}", nameof(type));

        DefaultName = name;
        Index = index;
        Type = type;
        DefaultAttributes = attributes;
        Owner = owner;
    }
}
