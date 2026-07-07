using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using StableNameDotNet.Providers;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents one managed type in the application.
/// </summary>
public class TypeAnalysisContext : HasGenericParameters, ITypeInfoProvider
{
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

    protected override int CustomAttributeIndex => Definition?.CustomAttributeIndex ?? -1;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringAssembly;

    public override string DefaultName => Definition?.Name! ?? throw new("Subclasses of TypeAnalysisContext must override DefaultName");

    public virtual string DefaultNamespace => Definition?.Namespace ?? throw new("Subclasses of TypeAnalysisContext must override DefaultNs");

    public virtual string? OverrideNamespace { get; set; }

    public string Namespace
    {
        get => OverrideNamespace ?? DefaultNamespace;
        set => OverrideNamespace = value;
    }

    public virtual TypeAttributes DefaultAttributes => Definition?.Attributes ?? TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed;

    public virtual TypeAttributes? OverrideAttributes { get; set; }

    public TypeAttributes Attributes
    {
        get => OverrideAttributes ?? DefaultAttributes;
        set => OverrideAttributes = value;
    }

    public virtual TypeAnalysisContext? DefaultBaseType => Definition == null || DefaultAttributes.HasFlag(TypeAttributes.Interface) ? null : AppContext.ResolveIl2CppType(Definition.RawBaseType);

    public TypeAnalysisContext? OverrideBaseType { get; set; }

    public TypeAnalysisContext? BaseType
    {
        get => OverrideBaseType ?? DefaultBaseType;
        set => OverrideBaseType = value;
    }

    public TypeAnalysisContext? DeclaringType { get; protected internal set; }

    public TypeAnalysisContext? EnumUnderlyingType => Definition == null ? null : AppContext.ResolveIl2CppType(Definition.EnumUnderlyingType);

    private List<TypeAnalysisContext>? _interfaceContexts;
    public List<TypeAnalysisContext> InterfaceContexts
    {
        get
        {
            // Lazy load the interface contexts
            _interfaceContexts ??= (Definition?.RawInterfaces.Select(AppContext.ResolveIl2CppType).ToList() ?? [])!;
            return _interfaceContexts;
        }
    }

    private List<GenericParameterTypeAnalysisContext>? _genericParameters;
    public override List<GenericParameterTypeAnalysisContext> GenericParameters
    {
        get
        {
            // Lazy load the generic parameters
            _genericParameters ??= Definition?.GenericContainer?.GenericParameters.Select(g => new GenericParameterTypeAnalysisContext(g, this)).ToList() ?? [];
            return _genericParameters;
        }
    }

    public virtual Il2CppTypeEnum Type
    {
        get
        {
            if (AppContext.SystemTypes.TryGetIl2CppTypeEnum(this, out var value))
                return value;

            if (IsEnumType)
                return Il2CppTypeEnum.IL2CPP_TYPE_ENUM;

            if (IsValueType)
                return Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE;

            return Il2CppTypeEnum.IL2CPP_TYPE_CLASS;
        }
    }

    public string DefaultFullName
    {
        get
        {
            if (DeclaringType != null)
                return DeclaringType.DefaultFullName + "+" + DefaultName;

            if (string.IsNullOrEmpty(DefaultNamespace))
                return DefaultName;

            return $"{DefaultNamespace}.{DefaultName}";
        }
    }

    public string FullName
    {
        get
        {
            if (DeclaringType != null)
                return DeclaringType.FullName + "+" + Name;

            if (string.IsNullOrEmpty(Namespace))
                return Name;

            return $"{Namespace}.{Name}";
        }
    }

    public TypeAttributes Visibility
    {
        get
        {
            return Attributes & TypeAttributes.VisibilityMask;
        }
        set
        {
            Attributes = (Attributes & ~TypeAttributes.VisibilityMask) | (value & TypeAttributes.VisibilityMask);
        }
    }

    public bool IsInterface => (Attributes & TypeAttributes.Interface) != default;
    public bool IsAbstract => (Attributes & TypeAttributes.Abstract) != default;
    public bool IsSealed => (Attributes & TypeAttributes.Sealed) != default;
    public bool IsStatic => IsAbstract && IsSealed;
    public bool IsDelegate => BaseType is not ReferencedTypeAnalysisContext and { Namespace: "System", Name: "MulticastDelegate" };

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

    public override string ToString() => $"Type: {FullName}";

    public virtual string GetCSharpSourceString()
    {
        if (Definition != null)
            return Definition.FullName!;

        var ret = new StringBuilder();
        if (OverrideNamespace != null)
            ret.Append(OverrideNamespace).Append('.');

        ret.Append(Name);

        return ret.ToString();
    }

    public ArrayTypeAnalysisContext MakeArrayType(int rank)
    {
        return new(this, rank);
    }

    public ByRefTypeAnalysisContext MakeByReferenceType()
    {
        return new(this);
    }

    public GenericInstanceTypeAnalysisContext MakeGenericInstanceType(params IEnumerable<TypeAnalysisContext> genericArguments)
    {
        return new(this, genericArguments);
    }

    public PointerTypeAnalysisContext MakePointerType()
    {
        return new(this);
    }

    public SzArrayTypeAnalysisContext MakeSzArrayType()
    {
        return new(this);
    }

    public PinnedTypeAnalysisContext MakePinnedType()
    {
        return new(this);
    }

    public BoxedTypeAnalysisContext MakeBoxedType()
    {
        return new(this);
    }

    public CustomModifierTypeAnalysisContext MakeCustomModifierType(TypeAnalysisContext modifierType, bool required)
    {
        return new(this, modifierType, required);
    }

    public InjectedTypeAnalysisContext InjectNestedType(string name, TypeAnalysisContext? baseType, TypeAttributes typeAttributes = TypeAttributes.NestedPublic | TypeAttributes.Sealed)
    {
        if (this is ReferencedTypeAnalysisContext)
            throw new InvalidOperationException("Cannot inject nested types into a non type definition");

        var ret = new InjectedTypeAnalysisContext(DeclaringAssembly, "", name, baseType, typeAttributes);
        ret.DeclaringType = this;
        NestedTypes.Add(ret);
        DeclaringAssembly.InjectType(ret);
        return ret;
    }

    #region StableNameDotNet implementation

    public IEnumerable<ITypeInfoProvider> GetBaseTypeHierarchy()
    {
        if (IsInjected)
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

            var genericParamTypes = genericClass.Context.ClassInst!.Types;

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
            return appContext.ResolveContextForType(appContext.LibCpp2IlContext.ReflectionCache.PrimitiveTypeDefinitions[type.Type])!;

        return appContext.ResolveContextForType(type.AsClass())!;
    }

    IEnumerable<ITypeInfoProvider> ITypeInfoProvider.Interfaces => Definition!.RawInterfaces!.Select(t => GetSndnProviderForType(AppContext, t));
    TypeAttributes ITypeInfoProvider.TypeAttributes => Attributes;
    int ITypeInfoProvider.GenericParameterCount => GenericParameters.Count;
    string ITypeInfoProvider.OriginalTypeName => DefaultName;
    string ITypeInfoProvider.RewrittenTypeName => Name;
    string ITypeInfoProvider.TypeNamespace => Namespace;
    public virtual bool IsGenericInstance => false;
    public virtual bool IsValueType
    {
        get
        {
            if (Definition is not null)
                return Definition.IsValueType;

            if (BaseType is { Namespace: "System", Name: "ValueType" })
                return Namespace is not "System" || Name is not "Enum"; // Enum is a reference type

            return IsEnumType;
        }
    }

    public bool IsEnumType => Definition?.IsEnumType ?? BaseType is { Namespace: "System", Name: "Enum" };
    IEnumerable<ITypeInfoProvider> ITypeInfoProvider.GenericArgumentInfoProviders => [];
    IEnumerable<IFieldInfoProvider> ITypeInfoProvider.FieldInfoProviders => Fields;
    IEnumerable<IMethodInfoProvider> ITypeInfoProvider.MethodInfoProviders => Methods;
    IEnumerable<IPropertyInfoProvider> ITypeInfoProvider.PropertyInfoProviders => Properties;
    ITypeInfoProvider? ITypeInfoProvider.DeclaringTypeInfoProvider => DeclaringType;

    #endregion
}
