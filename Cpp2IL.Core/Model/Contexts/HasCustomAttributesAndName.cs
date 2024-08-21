namespace Cpp2IL.Core.Model.Contexts;

public abstract class HasCustomAttributesAndName(uint token, ApplicationAnalysisContext appContext)
    : HasCustomAttributes(token, appContext)
{
    public abstract string DefaultName { get; }
    
    public string? OverrideName { get; set; }
    
    public string Name => OverrideName ?? DefaultName;

    public sealed override string CustomAttributeOwnerName => Name;
}