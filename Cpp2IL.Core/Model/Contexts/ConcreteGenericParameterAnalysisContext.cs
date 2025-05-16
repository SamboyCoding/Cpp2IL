using System;
using System.Diagnostics;
using System.Reflection;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericParameterAnalysisContext : ParameterAnalysisContext
{
    public ParameterAnalysisContext BaseParameterContext { get; }
    public override TypeAnalysisContext ParameterTypeContext { get; }
    public override ParameterAttributes ParameterAttributes => BaseParameterContext.ParameterAttributes;
    public override Il2CppType ParameterType => throw new NotSupportedException($"Instantiated generic parameters don't have an {nameof(Il2CppType)}");
    public override string DefaultName => BaseParameterContext.DefaultName;
    public override string? OverrideName { get => BaseParameterContext.OverrideName; set => BaseParameterContext.OverrideName = value; }
    protected override int CustomAttributeIndex => -1;

    public ConcreteGenericParameterAnalysisContext(ParameterAnalysisContext baseParameter, TypeAnalysisContext parameterType, ConcreteGenericMethodAnalysisContext declaringMethod) : base(null, baseParameter.ParamIndex, declaringMethod)
    {
        BaseParameterContext = baseParameter;
        ParameterTypeContext = parameterType;
    }
}
