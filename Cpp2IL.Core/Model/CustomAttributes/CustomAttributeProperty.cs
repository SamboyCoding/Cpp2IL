using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;

public class CustomAttributeProperty
{
    public readonly PropertyAnalysisContext Property;
    public readonly BaseCustomAttributeParameter Value;

    public CustomAttributeProperty(PropertyAnalysisContext property, BaseCustomAttributeParameter value)
    {
        Property = property;
        Value = value;
    }

    public override string ToString()
    {
        return $"{Property.Name} = {Value}";
    }
}