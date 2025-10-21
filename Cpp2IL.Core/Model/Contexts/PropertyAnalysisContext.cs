using System;
using System.Reflection;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Metadata;
using StableNameDotNet.Providers;

namespace Cpp2IL.Core.Model.Contexts;

public class PropertyAnalysisContext : HasCustomAttributesAndName, IPropertyInfoProvider
{
    public TypeAnalysisContext DeclaringType { get; }
    public Il2CppPropertyDefinition? Definition { get; }

    public MethodAnalysisContext? Getter { get; }
    public MethodAnalysisContext? Setter { get; }

    protected override int CustomAttributeIndex => Definition?.customAttributeIndex ?? -1;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType.DeclaringAssembly;

    public override string DefaultName => Definition?.Name ?? throw new($"Subclasses must override {nameof(DefaultName)}.");

    public virtual bool IsStatic => Definition?.IsStatic ?? throw new($"Subclasses must override {nameof(IsStatic)}.");

    public virtual PropertyAttributes DefaultAttributes => (PropertyAttributes?)Definition?.attrs ?? throw new($"Subclasses must override {nameof(DefaultAttributes)}.");

    public PropertyAttributes? OverrideAttributes { get; set; }

    public PropertyAttributes Attributes
    {
        get => OverrideAttributes ?? DefaultAttributes;
        set => OverrideAttributes = value;
    }

    public virtual TypeAnalysisContext DefaultPropertyType => DeclaringType.DeclaringAssembly.ResolveIl2CppType(Definition?.RawPropertyType)
        ?? throw new($"Subclasses must override {nameof(DefaultPropertyType)}.");

    public TypeAnalysisContext? OverridePropertyType { get; set; }

    public TypeAnalysisContext PropertyType
    {
        get => OverridePropertyType ?? DefaultPropertyType;
        set => OverridePropertyType = value;
    }

    public PropertyAnalysisContext(Il2CppPropertyDefinition definition, TypeAnalysisContext parent) : base(definition.token, parent.AppContext)
    {
        DeclaringType = parent;
        Definition = definition;

        InitCustomAttributeData();

        Getter = parent.GetMethod(definition.Getter);
        Setter = parent.GetMethod(definition.Setter);
    }

    protected PropertyAnalysisContext(MethodAnalysisContext? getter, MethodAnalysisContext? setter, TypeAnalysisContext parent) : base(0, parent.AppContext)
    {
        if (getter is null && setter is null)
            throw new ArgumentException("Property must have at least one method");

        DeclaringType = parent;
        Getter = getter;
        Setter = setter;
    }

    public override string ToString() => $"Property:  {DeclaringType.Name}::{Name}";

    #region StableNameDotNet implementation

    public ITypeInfoProvider PropertyTypeInfoProvider
        => Definition!.RawPropertyType!.ThisOrElementIsGenericParam()
            ? new GenericParameterTypeInfoProviderWrapper(Definition.RawPropertyType!.GetGenericParamName())
            : TypeAnalysisContext.GetSndnProviderForType(AppContext, Definition.RawPropertyType!);

    public string PropertyName => Name;

    #endregion
}
