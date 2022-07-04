using System;
using System.Collections.Generic;
using System.Reflection;

namespace StableNameDotNet.Providers;

public class PointerTypeInfoProviderWrapper : ITypeInfoProvider
{
    public ITypeInfoProvider ElementTypeProvider { get; }

    public PointerTypeInfoProviderWrapper(ITypeInfoProvider elementTypeProvider)
    {
        ElementTypeProvider = elementTypeProvider;
    }

    public IEnumerable<ITypeInfoProvider> GetBaseTypeHierarchy() => ElementTypeProvider.GetBaseTypeHierarchy();

    public IEnumerable<ITypeInfoProvider> Interfaces => ElementTypeProvider.Interfaces;
    public TypeAttributes TypeAttributes => ElementTypeProvider.TypeAttributes;
    public string OriginalTypeName => ElementTypeProvider.OriginalTypeName;
    public bool IsGenericInstance => false;
    public bool IsValueType => ElementTypeProvider.IsValueType;
    public string TypeNamespace => ElementTypeProvider.TypeNamespace;
    public string RewrittenTypeName => ElementTypeProvider.RewrittenTypeName;
    public bool IsEnumType => false;
    public int GenericParameterCount => 0;
    public IEnumerable<ITypeInfoProvider> GenericArgumentInfoProviders => Array.Empty<ITypeInfoProvider>();
    public IEnumerable<IFieldInfoProvider> FieldInfoProviders => ElementTypeProvider.FieldInfoProviders;
    public IEnumerable<IMethodInfoProvider> MethodInfoProviders => ElementTypeProvider.MethodInfoProviders;
    public IEnumerable<IPropertyInfoProvider> PropertyInfoProviders => ElementTypeProvider.PropertyInfoProviders;
}