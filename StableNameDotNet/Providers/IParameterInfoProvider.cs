namespace StableNameDotNet.Providers;

public interface IParameterInfoProvider
{
    public ITypeInfoProvider ParameterTypeInfoProvider { get; }
    public string ParameterName { get; }
}