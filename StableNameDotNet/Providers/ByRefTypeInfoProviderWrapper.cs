using System;
using System.Collections.Generic;
using System.Reflection;

namespace StableNameDotNet.Providers;

public class ByRefTypeInfoProviderWrapper : ITypeInfoProvider
{
    public ITypeInfoProvider ElementTypeProvider { get; }

    public ByRefTypeInfoProviderWrapper(ITypeInfoProvider elementTypeProvider)
    {
        ElementTypeProvider = elementTypeProvider;
    }

    public IEnumerable<ITypeInfoProvider> GetBaseTypeHierarchy() => ElementTypeProvider.GetBaseTypeHierarchy();

    public IEnumerable<ITypeInfoProvider> Interfaces => ElementTypeProvider.Interfaces;
    public TypeAttributes TypeAttributes => ElementTypeProvider.TypeAttributes;
    public string OriginalTypeName => ElementTypeProvider.OriginalTypeName;
    public string RewrittenTypeName => ElementTypeProvider.RewrittenTypeName;
    public string TypeNamespace => ElementTypeProvider.TypeNamespace;
    public bool IsGenericInstance => false;
    public bool IsValueType => ElementTypeProvider.IsValueType;
    public bool IsEnumType => false;
    public int GenericParameterCount => 0;
    public IEnumerable<ITypeInfoProvider> GenericArgumentInfoProviders => Array.Empty<ITypeInfoProvider>();
    public IEnumerable<IFieldInfoProvider> FieldInfoProviders => ElementTypeProvider.FieldInfoProviders;
    public IEnumerable<IMethodInfoProvider> MethodInfoProviders => ElementTypeProvider.MethodInfoProviders;
    public IEnumerable<IPropertyInfoProvider> PropertyInfoProviders => ElementTypeProvider.PropertyInfoProviders;
}