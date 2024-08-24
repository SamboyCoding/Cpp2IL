using System.Collections.Generic;
using System.Reflection;

namespace StableNameDotNet.Providers;

public class GenericInstanceTypeInfoProviderWrapper(
    ITypeInfoProvider elementTypeProvider,
    ITypeInfoProvider[] genericTypeProviders)
    : ITypeInfoProvider
{
    public ITypeInfoProvider ElementTypeProvider { get; } = elementTypeProvider;
    public ITypeInfoProvider[] GenericTypeProviders { get; } = genericTypeProviders;

    public ITypeInfoProvider? DeclaringTypeInfoProvider => ElementTypeProvider.DeclaringTypeInfoProvider;
    public IEnumerable<ITypeInfoProvider> GetBaseTypeHierarchy() => ElementTypeProvider.GetBaseTypeHierarchy();

    public IEnumerable<ITypeInfoProvider> Interfaces => ElementTypeProvider.Interfaces;
    public TypeAttributes TypeAttributes => ElementTypeProvider.TypeAttributes;
    public string OriginalTypeName => ElementTypeProvider.OriginalTypeName;
    public string TypeNamespace => ElementTypeProvider.TypeNamespace;
    public string RewrittenTypeName => ElementTypeProvider.RewrittenTypeName;
    public bool IsGenericInstance => true;
    public bool IsValueType => ElementTypeProvider.IsValueType;
    public bool IsEnumType => false;
    public int GenericParameterCount => ElementTypeProvider.GenericParameterCount;
    public IEnumerable<ITypeInfoProvider> GenericArgumentInfoProviders => GenericTypeProviders;
    public IEnumerable<IFieldInfoProvider> FieldInfoProviders => ElementTypeProvider.FieldInfoProviders;
    public IEnumerable<IMethodInfoProvider> MethodInfoProviders => ElementTypeProvider.MethodInfoProviders;
    public IEnumerable<IPropertyInfoProvider> PropertyInfoProviders => ElementTypeProvider.PropertyInfoProviders;
}
