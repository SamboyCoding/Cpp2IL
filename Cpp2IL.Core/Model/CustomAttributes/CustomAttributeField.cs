using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;

public class CustomAttributeField
{
    public readonly FieldAnalysisContext Field;
    public readonly BaseCustomAttributeParameter Value;

    public CustomAttributeField(FieldAnalysisContext field, BaseCustomAttributeParameter value)
    {
        Field = field;
        Value = value;
    }
}