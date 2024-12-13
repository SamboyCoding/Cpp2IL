using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
using StableNameDotNet.Providers;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents one managed type in the application.
/// </summary>
public class TypeAnalysisContext : HasCustomAttributesAndName, ITypeInfoProvider, ICSharpSourceToken
{
    internal const TypeAttributes DefaultTypeAttributes = TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed;

    /// <summary>
    /// The context for the assembly this type was defined in.
    /// </summary>
    public readonly AssemblyAnalysisContext DeclaringAssembly;

    /// <summary>
    /// The underlying metadata for this type. Allows access to RGCTX data, the raw bitfield properties, interfaces, etc.
    /// </summary>
    public readonly Il2CppTypeDefinition? Definition;

    /// <summary>
    /// The analysis contexts for methods contained within this type.
    /// </summary>
    public readonly List<MethodAnalysisContext> Methods;

    /// <summary>
    /// The analysis contexts for properties contained within this type.
    /// </summary>
    public readonly List<PropertyAnalysisContext> Properties;

    /// <summary>
    /// The analysis contexts for events contained within this type.
    /// </summary>
    public readonly List<EventAnalysisContext> Events;

    /// <summary>
    /// The analysis contexts for fields contained within this type.
    /// </summary>
    public readonly List<FieldAnalysisContext> Fields;

    /// <summary>
    /// The analysis contexts for nested types within this type.
    /// </summary>
    public List<TypeAnalysisContext> NestedTypes { get; internal set; } = [];

    protected override int CustomAttributeIndex => Definition!.CustomAttributeIndex;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringAssembly;

    public override string DefaultName => Definition?.Name! ?? throw new("Subclasses of TypeAnalysisContext must override DefaultName");

    public virtual string DefaultNs => Definition?.Namespace ?? throw new("Subclasses of TypeAnalysisContext must override DefaultNs");

    public string? OverrideNs { get; set; }

    public string Namespace => OverrideNs ?? DefaultNs;

    public TypeAnalysisContext? OverrideBaseType { get; protected set; }

    public TypeAnalysisContext? DeclaringType { get; protected internal set; }

    public TypeAnalysisContext? EnumUnderlyingType => Definition == null ? null : DeclaringAssembly.ResolveIl2CppType(Definition.EnumUnderlyingType);

    public TypeAnalysisContext? BaseType => OverrideBaseType ?? (Definition == null ? null : DeclaringAssembly.ResolveIl2CppType(Definition.RawBaseType));

    public TypeAnalysisContext[] InterfaceContexts => (Definition?.RawInterfaces.Select(DeclaringAssembly.ResolveIl2CppType).ToArray() ?? [])!;
    
    public bool IsPrimitive
    {
        get
        {
            if (Definition == null)
                return false;

            if (Definition.RawBaseType?.Type.IsIl2CppPrimitive() == true)
                return true;
            
            //Might still be TYPE_CLASS but yet int or something, so check it directly
            return AppContext.SystemTypes.IsPrimitive(this);
        }
    }

    public virtual Il2CppTypeEnum Type
    {
        get
        {
            if (Definition is { RawBaseType: not null })
                return Definition.RawBaseType.Type;

            if (AppContext.SystemTypes.TryGetIl2CppTypeEnum(this, out var value))
                return value;

            if (IsEnumType)
                return Il2CppTypeEnum.IL2CPP_TYPE_ENUM;

            if (IsValueType)
                return Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE;

            return Il2CppTypeEnum.IL2CPP_TYPE_CLASS;
        }
    }

    public string FullName
    {
        get
        {
            if (DeclaringType != null)
                return DeclaringType.FullName + "." + Name;

            if (string.IsNullOrEmpty(Namespace))
                return Name;

            return $"{Namespace}.{Name}";
        }
    }

    /// <summary>
    /// Returns the namespace of this type expressed as a folder hierarchy, with each sub-namespace becoming a sub-directory.
    /// If this type is in the global namespace, this will return an empty string.
    /// </summary>
    public string NamespaceAsSubdirs
    {
        get
        {
            var ns = Namespace;
            return string.IsNullOrEmpty(ns) ? "" : Path.Combine(MiscUtils.CleanPathElement(ns).Split('.'));
        }
    }

    /// <summary>
    /// Returns the top-level type this type is nested inside. If this type is not nested, will return this type.
    /// </summary>
    public TypeAnalysisContext UltimateDeclaringType => DeclaringType ?? this;

    public TypeAnalysisContext(Il2CppTypeDefinition? il2CppTypeDefinition, AssemblyAnalysisContext containingAssembly) : base(il2CppTypeDefinition?.Token ?? 0, containingAssembly.AppContext)
    {
        DeclaringAssembly = containingAssembly;
        Definition = il2CppTypeDefinition;

        if (Definition != null)
        {
            InitCustomAttributeData();

            Methods = Definition.Methods!.Select(m => new MethodAnalysisContext(m, this)).ToList();
            Properties = Definition.Properties!.Select(p => new PropertyAnalysisContext(p, this)).ToList();
            Events = Definition.Events!.Select(e => new EventAnalysisContext(e, this)).ToList();
            Fields = Definition.FieldInfos!.ToList().Select(f => new FieldAnalysisContext(f, this)).ToList();
        }
        else
        {
            Methods = [];
            Properties = [];
            Events = [];
            Fields = [];
        }
    }

    public MethodAnalysisContext? GetMethod(Il2CppMethodDefinition? methodDefinition)
    {
        if (methodDefinition == null)
            return null;

        return Methods.Find(m => m.Definition == methodDefinition);
    }

    public List<MethodAnalysisContext> GetConstructors() => Methods.Where(m => m.Definition!.Name == ".ctor").ToList();

    public override string ToString() => $"Type: {Definition?.FullName}";

    public virtual string GetCSharpSourceString()
    {
        if (Definition != null)
            return Definition.FullName!;

        var ret = new StringBuilder();
        if (OverrideNs != null)
            ret.Append(OverrideNs).Append('.');

        ret.Append(Name);

        return ret.ToString();
    }

    public ArrayTypeAnalysisContext MakeArrayType(int rank)
    {
        return new(this, rank, DeclaringAssembly);
    }

    public ByRefTypeAnalysisContext MakeByReferenceType()
    {
        return new(this, DeclaringAssembly);
    }

    public PointerTypeAnalysisContext MakePointerType()
    {
        return new(this, DeclaringAssembly);
    }

    public SzArrayTypeAnalysisContext MakeSzArrayType()
    {
        return new(this, DeclaringAssembly);
    }

    #region StableNameDotNet implementation

    public IEnumerable<ITypeInfoProvider> GetBaseTypeHierarchy()
    {
        if (OverrideBaseType != null)
            throw new("Type hierarchy for injected types is not supported");

        var baseType = Definition!.RawBaseType;
        while (baseType != null)
        {
            yield return GetSndnProviderForType(AppContext, baseType);

            baseType = baseType.CoerceToUnderlyingTypeDefinition().RawBaseType;
        }
    }

    public static ITypeInfoProvider GetSndnProviderForType(ApplicationAnalysisContext appContext, Il2CppType type)
    {
        if (type.Type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
        {
            var genericClass = type.GetGenericClass();
            var elementType = appContext.ResolveContextForType(genericClass.TypeDefinition)!;

            var genericParamTypes = genericClass.Context.ClassInst.Types;

            if (genericParamTypes.Any(t => t.Type is Il2CppTypeEnum.IL2CPP_TYPE_VAR or Il2CppTypeEnum.IL2CPP_TYPE_MVAR))
                //Discard non-fixed generic instances
                return elementType;

            var genericArguments = genericParamTypes.Select(t => GetSndnProviderForType(appContext, t)).ToArray();

            return new GenericInstanceTypeInfoProviderWrapper(elementType, genericArguments);
        }

        if (type.Type is Il2CppTypeEnum.IL2CPP_TYPE_VAR or Il2CppTypeEnum.IL2CPP_TYPE_MVAR)
            return new GenericParameterTypeInfoProviderWrapper(type.GetGenericParameterDef().Name!);

        if (type.Type is Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY or Il2CppTypeEnum.IL2CPP_TYPE_PTR)
            return GetSndnProviderForType(appContext, type.GetEncapsulatedType());

        if (type.Type is Il2CppTypeEnum.IL2CPP_TYPE_ARRAY)
            return GetSndnProviderForType(appContext, type.GetArrayElementType());

        if (type.Type.IsIl2CppPrimitive())
            return appContext.ResolveContextForType(LibCpp2IlReflection.PrimitiveTypeDefinitions[type.Type])!;

        return appContext.ResolveContextForType(type.AsClass())!;
    }

    public IEnumerable<ITypeInfoProvider> Interfaces => Definition!.RawInterfaces!.Select(t => GetSndnProviderForType(AppContext, t));
    public virtual TypeAttributes TypeAttributes => Definition?.Attributes ?? DefaultTypeAttributes;
    public virtual int GenericParameterCount => Definition!.GenericContainer?.genericParameterCount ?? 0;
    public string OriginalTypeName => DefaultName;
    public string RewrittenTypeName => Name;
    public string TypeNamespace => Namespace;
    public virtual bool IsGenericInstance => false;
    public virtual bool IsValueType => Definition?.IsValueType ?? BaseType is { Namespace: "System", Name: "ValueType" };
    public bool IsEnumType => Definition?.IsEnumType ?? BaseType is { Namespace: "System", Name: "Enum" };
    public bool IsInterface => Definition?.IsInterface ?? ((TypeAttributes & TypeAttributes.Interface) != default);
    public IEnumerable<ITypeInfoProvider> GenericArgumentInfoProviders => Array.Empty<ITypeInfoProvider>();
    public IEnumerable<IFieldInfoProvider> FieldInfoProviders => Fields;
    public IEnumerable<IMethodInfoProvider> MethodInfoProviders => Methods;
    public IEnumerable<IPropertyInfoProvider> PropertyInfoProviders => Properties;
    public ITypeInfoProvider? DeclaringTypeInfoProvider => DeclaringType;

    #endregion
}
