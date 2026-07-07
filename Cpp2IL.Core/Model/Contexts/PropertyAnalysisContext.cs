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

    public bool IsAbstract => Getter?.IsAbstract ?? Setter?.IsAbstract ?? false;

    public bool IsVirtual => Getter?.IsVirtual ?? Setter?.IsVirtual ?? false;

    public bool IsNewSlot => Getter?.IsNewSlot ?? Setter?.IsNewSlot ?? false;

    public bool IsFinal => Getter?.IsFinal ?? Setter?.IsFinal ?? false;

    public virtual PropertyAttributes DefaultAttributes => (PropertyAttributes?)Definition?.attrs ?? throw new($"Subclasses must override {nameof(DefaultAttributes)}.");

    public PropertyAttributes? OverrideAttributes { get; set; }

    public PropertyAttributes Attributes
    {
        get => OverrideAttributes ?? DefaultAttributes;
        set => OverrideAttributes = value;
    }

    public virtual TypeAnalysisContext DefaultPropertyType => AppContext.ResolveIl2CppType(Definition?.RawPropertyType)
        ?? throw new($"Subclasses must override {nameof(DefaultPropertyType)}.");

    public TypeAnalysisContext? OverridePropertyType { get; set; }

    public TypeAnalysisContext PropertyType
    {
        get => OverridePropertyType ?? DefaultPropertyType;
        set => OverridePropertyType = value;
    }

    public MethodAttributes Visibility
    {
        get
        {
            // Determine the most permissive visibility among the getter and setter
            var getterVisibility = Getter?.Visibility ?? MethodAttributes.PrivateScope;
            var setterVisibility = Setter?.Visibility ?? MethodAttributes.PrivateScope;

            // public
            if (getterVisibility == MethodAttributes.Public || setterVisibility == MethodAttributes.Public)
                return MethodAttributes.Public;

            // protected internal
            if (getterVisibility == MethodAttributes.FamORAssem || setterVisibility == MethodAttributes.FamORAssem)
                return MethodAttributes.FamORAssem;

            // internal
            if (getterVisibility == MethodAttributes.Assembly || setterVisibility == MethodAttributes.Assembly)
                return MethodAttributes.Assembly;

            // protected
            if (getterVisibility == MethodAttributes.Family || setterVisibility == MethodAttributes.Family)
                return MethodAttributes.Family;

            // private protected
            if (getterVisibility == MethodAttributes.FamANDAssem || setterVisibility == MethodAttributes.FamANDAssem)
                return MethodAttributes.FamANDAssem;

            // private
            return MethodAttributes.Private;
        }
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
