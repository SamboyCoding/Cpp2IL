namespace Cpp2IL.Core.Api;

public abstract class Cpp2IlPlugin
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    
    public abstract void OnLoad();
}