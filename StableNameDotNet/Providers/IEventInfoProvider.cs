namespace StableNameDotNet.Providers;

public interface IEventInfoProvider
{
    public ITypeInfoProvider EventTypeInfoProvider { get; }
    public string EventName { get; }
}