using System;
using System.Collections.Generic;
using System.Reflection;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;
using StableNameDotNet.Providers;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents a field in a managed type.
/// </summary>
public class FieldAnalysisContext : HasCustomAttributesAndName, IFieldInfoProvider
{
    /// <summary>
    /// The analysis context for the type that this field belongs to.
    /// </summary>
    public readonly TypeAnalysisContext DeclaringType;

    /// <summary>
    /// The underlying field metadata.
    /// </summary>
    public readonly Il2CppFieldReflectionData? BackingData;

    protected override int CustomAttributeIndex => BackingData?.Field.customAttributeIndex ?? -1;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType.DeclaringAssembly;

    public override string DefaultName => BackingData?.Field.Name!;

    private Il2CppType? RawFieldType => BackingData?.Field.RawFieldType;

    public virtual FieldAttributes DefaultAttributes => BackingData?.Attributes ?? throw new($"Subclass must override {nameof(DefaultAttributes)}");

    public virtual FieldAttributes? OverrideAttributes { get; set; }

    public FieldAttributes Attributes
    {
        get => OverrideAttributes ?? DefaultAttributes;
        set => OverrideAttributes = value;
    }

    public bool IsStatic => (Attributes & FieldAttributes.Static) != 0;

    public virtual object? DefaultConstantValue => BackingData?.Field.DefaultValue?.Value;

    public virtual object? OverrideConstantValue { get; set; }

    public object? ConstantValue
    {
        get => OverrideConstantValue ?? DefaultConstantValue;
        set => OverrideConstantValue = value;
    }

    public virtual int DefaultOffset => BackingData?.FieldOffset ?? -1;

    public virtual int? OverrideOffset { get; set; }

    public int Offset
    {
        get => OverrideOffset ?? DefaultOffset;
        set => OverrideOffset = value;
    }

    public virtual TypeAnalysisContext DefaultFieldType => AppContext.ResolveIl2CppType(RawFieldType)
                                                           ?? throw new($"Field type {RawFieldType} could not be resolved.");

    public TypeAnalysisContext? OverrideFieldType { get; set; }

    public TypeAnalysisContext FieldType
    {
        get => OverrideFieldType ?? DefaultFieldType;
        set => OverrideFieldType = value;
    }

    public virtual byte[] DefaultStaticArrayInitialValue => BackingData?.Field.StaticArrayInitialValue ?? [];

    public virtual byte[]? OverrideStaticArrayInitialValue { get; set; }

    public byte[] StaticArrayInitialValue
    {
        get => OverrideStaticArrayInitialValue ?? DefaultStaticArrayInitialValue;
        set => OverrideStaticArrayInitialValue = value;
    }

    public FieldAttributes Visibility
    {
        get
        {
            return Attributes & FieldAttributes.FieldAccessMask;
        }
        set
        {
            Attributes = (Attributes & ~FieldAttributes.FieldAccessMask) | (value & FieldAttributes.FieldAccessMask);
        }
    }


    public FieldAnalysisContext(Il2CppFieldReflectionData? backingData, TypeAnalysisContext parent) : base(backingData?.Field.token ?? 0, parent.AppContext)
    {
        DeclaringType = parent;
        BackingData = backingData;

        if (BackingData != null)
            InitCustomAttributeData();
    }

    public ConcreteGenericFieldAnalysisContext MakeConcreteGenericField(params IEnumerable<TypeAnalysisContext> typeGenericParameters)
    {
        if (this is ConcreteGenericFieldAnalysisContext)
        {
            throw new InvalidOperationException($"Attempted to make a {nameof(ConcreteGenericFieldAnalysisContext)} concrete: {this}");
        }
        else
        {
            return new ConcreteGenericFieldAnalysisContext(this, typeGenericParameters);
        }
    }

    public override string ToString() => $"Field: {DeclaringType.Name}::{Name}";

    #region StableNameDotNet

    ITypeInfoProvider IFieldInfoProvider.FieldTypeInfoProvider
        => GetGenericParamName(FieldType) is { } name
            ? new GenericParameterTypeInfoProviderWrapper(name)
            : TypeAnalysisContext.GetSndnProviderForType(AppContext, RawFieldType!);

    string IFieldInfoProvider.FieldName => Name;

    FieldAttributes IFieldInfoProvider.FieldAttributes => Attributes;

    private static string? GetGenericParamName(TypeAnalysisContext type) => type switch
    {
        GenericParameterTypeAnalysisContext genericParam => genericParam.Name,
        WrappedTypeAnalysisContext wrapped => GetGenericParamName(wrapped.ElementType),
        _ => null
    };

    #endregion
}
