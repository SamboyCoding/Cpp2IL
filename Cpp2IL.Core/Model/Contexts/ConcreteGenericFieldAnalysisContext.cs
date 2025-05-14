using System.Reflection;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericFieldAnalysisContext : FieldAnalysisContext
{
    public FieldAnalysisContext BaseFieldContext { get; }
    public override FieldAttributes Attributes => BaseFieldContext.Attributes;
    public override TypeAnalysisContext FieldTypeContext { get; }
    public override string DefaultName => BaseFieldContext.DefaultName;
    public override string? OverrideName { get => BaseFieldContext.OverrideName; set => BaseFieldContext.OverrideName = value; }

    public ConcreteGenericFieldAnalysisContext(FieldAnalysisContext baseField, GenericInstanceTypeAnalysisContext genericInstanceType)
        : base(null, genericInstanceType)
    {
        BaseFieldContext = baseField;
        FieldTypeContext = GenericInstantiation.Instantiate(baseField.FieldTypeContext, genericInstanceType.GenericArguments, []);
    }
}
