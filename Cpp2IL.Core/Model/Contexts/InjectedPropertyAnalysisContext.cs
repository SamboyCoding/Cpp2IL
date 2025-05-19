using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedPropertyAnalysisContext : PropertyAnalysisContext
{
    public override string DefaultName { get; }
    public override PropertyAttributes DefaultAttributes { get; }
    public override TypeAnalysisContext DefaultPropertyType { get; }
    public override bool IsStatic
    {
        get
        {
            if (Getter is not null)
                return Getter.IsStatic;
            if (Setter is not null)
                return Setter.IsStatic;
            throw new("Property has no methods");
        }
    }
    protected override bool IsInjected => true;

    public InjectedPropertyAnalysisContext(
        string name,
        TypeAnalysisContext propertyType,
        MethodAnalysisContext? getter,
        MethodAnalysisContext? setter,
        PropertyAttributes propertyAttributes,
        TypeAnalysisContext parent) : base(getter, setter, parent)
    {
        DefaultName = name;
        DefaultPropertyType = propertyType;
        DefaultAttributes = propertyAttributes;
    }
}
