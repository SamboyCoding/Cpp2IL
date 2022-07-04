using System;
using System.Collections.Generic;
using System.Reflection;

namespace StableNameDotNet.Providers;

public class GenericParameterTypeInfoProviderWrapper : ITypeInfoProvider
{
    public readonly string GenericParameterName;

    public GenericParameterTypeInfoProviderWrapper(string genericParameterName)
    {
        GenericParameterName = genericParameterName;
    }

    public IEnumerable<ITypeInfoProvider> GetBaseTypeHierarchy() => Array.Empty<ITypeInfoProvider>();

    public IEnumerable<ITypeInfoProvider> Interfaces => Array.Empty<ITypeInfoProvider>();
    public TypeAttributes TypeAttributes => 0;
    public string OriginalTypeName => GenericParameterName;
    public string TypeNamespace => string.Empty;
    public string RewrittenTypeName => GenericParameterName;
    public bool IsGenericInstance => false;
    public bool IsValueType => false;
    public bool IsEnumType => false;
    public int GenericParameterCount => 0;
    public IEnumerable<ITypeInfoProvider> GenericArgumentInfoProviders => Array.Empty<ITypeInfoProvider>();
    public IEnumerable<IFieldInfoProvider> FieldInfoProviders => Array.Empty<IFieldInfoProvider>();
    public IEnumerable<IMethodInfoProvider> MethodInfoProviders => Array.Empty<IMethodInfoProvider>();
    public IEnumerable<IPropertyInfoProvider> PropertyInfoProviders => Array.Empty<IPropertyInfoProvider>();
}