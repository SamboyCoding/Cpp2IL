namespace StableNameDotNet.Providers;

public interface IFieldInfoProvider
{
    public ITypeInfoProvider FieldTypeInfoProvider { get; }
    
    public string FieldName { get; }
}