namespace StableNameDotNet.Providers;

public interface IPropertyInfoProvider
{
    public ITypeInfoProvider PropertyTypeInfoProvider { get; }

    public string PropertyName { get; }
}
