using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericParameterAnalysisContext : ParameterAnalysisContext
{
    public ParameterAnalysisContext BaseParameterContext { get; }
    public override TypeAnalysisContext DefaultParameterType { get; }
    public override ParameterAttributes DefaultAttributes => BaseParameterContext.DefaultAttributes;
    public override ParameterAttributes? OverrideAttributes { get => BaseParameterContext.OverrideAttributes; set => BaseParameterContext.OverrideAttributes = value; }
    public override string DefaultName => BaseParameterContext.DefaultName;
    public override string? OverrideName { get => BaseParameterContext.OverrideName; set => BaseParameterContext.OverrideName = value; }
    protected override int CustomAttributeIndex => -1;

    public ConcreteGenericParameterAnalysisContext(ParameterAnalysisContext baseParameter, TypeAnalysisContext parameterType, ConcreteGenericMethodAnalysisContext declaringMethod) : base(null, baseParameter.ParameterIndex, declaringMethod)
    {
        BaseParameterContext = baseParameter;
        DefaultParameterType = parameterType;
    }
}
