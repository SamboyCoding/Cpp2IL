using System.Collections.Generic;
using System.Reflection;

namespace StableNameDotNet.Providers;

public class GenericInstanceTypeInfoProviderWrapper : ITypeInfoProvider
{
    public ITypeInfoProvider ElementTypeProvider { get; }
    public ITypeInfoProvider[] GenericTypeProviders { get; }

    public GenericInstanceTypeInfoProviderWrapper(ITypeInfoProvider elementTypeProvider, ITypeInfoProvider[] genericTypeProviders)
    {
        ElementTypeProvider = elementTypeProvider;
        GenericTypeProviders = genericTypeProviders;
    }

    public IEnumerable<ITypeInfoProvider> GetBaseTypeHierarchy() => ElementTypeProvider.GetBaseTypeHierarchy();

    public IEnumerable<ITypeInfoProvider> Interfaces => ElementTypeProvider.Interfaces;
    public TypeAttributes TypeAttributes => ElementTypeProvider.TypeAttributes;
    public string TypeName => ElementTypeProvider.TypeName;
    public bool IsGenericInstance => true;
    public bool IsValueType => ElementTypeProvider.IsValueType;
    public bool IsEnumType => false;
    public IEnumerable<ITypeInfoProvider> GenericArgumentInfoProviders => GenericTypeProviders;
    public IEnumerable<IFieldInfoProvider> FieldInfoProviders => ElementTypeProvider.FieldInfoProviders;
    public IEnumerable<IMethodInfoProvider> MethodInfoProviders => ElementTypeProvider.MethodInfoProviders;
    public IEnumerable<IPropertyInfoProvider> PropertyInfoProviders => ElementTypeProvider.PropertyInfoProviders;
}