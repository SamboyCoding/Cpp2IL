using System.Reflection;

namespace StableNameDotNet.Providers;

public interface IFieldInfoProvider
{
    public ITypeInfoProvider FieldTypeInfoProvider { get; }
    
    public FieldAttributes FieldAttributes { get; }
    
    public string FieldName { get; }
}