using System.Reflection;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericFieldAnalysisContext : FieldAnalysisContext
{
    public FieldAnalysisContext BaseFieldContext { get; }
    public override FieldAttributes DefaultAttributes => BaseFieldContext.DefaultAttributes;
    public override FieldAttributes? OverrideAttributes { get => BaseFieldContext.OverrideAttributes; set => BaseFieldContext.OverrideAttributes = value; }
    public override TypeAnalysisContext DefaultFieldType { get; }
    public override string DefaultName => BaseFieldContext.DefaultName;
    public override string? OverrideName { get => BaseFieldContext.OverrideName; set => BaseFieldContext.OverrideName = value; }

    public ConcreteGenericFieldAnalysisContext(FieldAnalysisContext baseField, GenericInstanceTypeAnalysisContext genericInstanceType)
        : base(null, genericInstanceType)
    {
        BaseFieldContext = baseField;
        DefaultFieldType = GenericInstantiation.Instantiate(baseField.FieldType, genericInstanceType.GenericArguments, []);
    }
}
