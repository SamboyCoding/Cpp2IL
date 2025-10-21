namespace Cpp2IL.Core.Model.Contexts;

public abstract class HasCustomAttributesAndName(uint token, ApplicationAnalysisContext appContext)
    : HasCustomAttributes(token, appContext)
{
    public abstract string DefaultName { get; }

    public virtual string? OverrideName { get; set; }

    public string Name
    {
        get => OverrideName ?? DefaultName;
        set => OverrideName = value;
    }
}
