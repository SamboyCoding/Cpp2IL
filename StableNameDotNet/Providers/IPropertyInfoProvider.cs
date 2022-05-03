namespace StableNameDotNet.Providers;

public interface IPropertyInfoProvider
{
    public ITypeInfoProvider PropertyType { get; }
    
    public string PropertyName { get; }
}