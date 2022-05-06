using System.Collections.Generic;
using System.Reflection;

namespace StableNameDotNet.Providers;

public interface ITypeInfoProvider
{
    /// <summary>
    /// Get the names of the base types for this type, in order. This should probably be a yielding function which returns each base type, one at a time (starting with the base type of this type,
    /// then the base type of that, etc.).
    /// </summary>
    public IEnumerable<ITypeInfoProvider> GetBaseTypeHierarchy();
    
    /// <summary>
    /// Get the names of the interfaces implemented by this type.
    /// </summary>
    public IEnumerable<ITypeInfoProvider> Interfaces { get; }
    
    /// <summary>
    /// Return the <see cref="TypeAttributes"/> for this type.
    /// </summary>
    public TypeAttributes TypeAttributes { get; }
    
    /// <summary>
    /// Returns the name of this type. If this type is generic, this should not include the generic arguments, but can include the backtick. If this type is an array, this should not include the array dimensions.
    /// </summary>
    public string TypeName { get; }
    
    /// <summary>
    /// Return true if this type is a generic instance - that is, a type with generic parameters *which has all of its generic parameters filled in*.
    /// </summary>
    public bool IsGenericInstance { get; }
    
    /// <summary>
    /// Return true if this is a value type, else false.
    /// </summary>
    public bool IsValueType { get; }
    
    /// <summary>
    /// Return true if this is an enum, else false.
    /// </summary>
    public bool IsEnumType { get; }
    
    /// <summary>
    /// Return the number of generic parameters for this type. Used to populate the backtick-suffix of a generic type name.
    /// </summary>
    public int GenericParameterCount { get; }
    
    /// <summary>
    /// Returns any generic arguments for this type. This should be an empty enumerable if this type is not a generic instance.
    /// </summary>
    public IEnumerable<ITypeInfoProvider> GenericArgumentInfoProviders { get; }

    public IEnumerable<IFieldInfoProvider> FieldInfoProviders { get; }
    
    public IEnumerable<IMethodInfoProvider> MethodInfoProviders { get; }
    
    public IEnumerable<IPropertyInfoProvider> PropertyInfoProviders { get; }
}