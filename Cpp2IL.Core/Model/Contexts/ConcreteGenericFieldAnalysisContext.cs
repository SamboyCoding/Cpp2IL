using System.Collections.Generic;
using System.Reflection;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericFieldAnalysisContext : FieldAnalysisContext
{
    public FieldAnalysisContext BaseFieldContext { get; }
    public override FieldAttributes DefaultAttributes => BaseFieldContext.DefaultAttributes;
    public override FieldAttributes? OverrideAttributes { get => BaseFieldContext.OverrideAttributes; set => BaseFieldContext.OverrideAttributes = value; }
    public override object? DefaultConstantValue => BaseFieldContext.DefaultConstantValue;
    public override object? OverrideConstantValue { get => BaseFieldContext.OverrideConstantValue; set => BaseFieldContext.OverrideConstantValue = value; }
    public override int DefaultOffset => BaseFieldContext.DefaultOffset;
    public override int? OverrideOffset { get => BaseFieldContext.OverrideOffset; set => BaseFieldContext.OverrideOffset = value; }
    public override byte[] DefaultStaticArrayInitialValue => BaseFieldContext.DefaultStaticArrayInitialValue;
    public override byte[]? OverrideStaticArrayInitialValue { get => BaseFieldContext.OverrideStaticArrayInitialValue; set => BaseFieldContext.OverrideStaticArrayInitialValue = value; }
    public override TypeAnalysisContext DefaultFieldType { get; }
    public override string DefaultName => BaseFieldContext.DefaultName;
    public override string? OverrideName { get => BaseFieldContext.OverrideName; set => BaseFieldContext.OverrideName = value; }

    public ConcreteGenericFieldAnalysisContext(FieldAnalysisContext baseField, GenericInstanceTypeAnalysisContext genericInstanceType)
        : base(null, genericInstanceType)
    {
        BaseFieldContext = baseField;
        DefaultFieldType = GenericInstantiation.Instantiate(baseField.FieldType, genericInstanceType.GenericArguments, []);
    }

    public ConcreteGenericFieldAnalysisContext(FieldAnalysisContext baseField, IEnumerable<TypeAnalysisContext> typeGenericParameters)
        : this(baseField, baseField.DeclaringType.MakeGenericInstanceType(typeGenericParameters))
    {
    }
}
