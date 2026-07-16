using System.Reflection;
using LibCpp2IL;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedParameterAnalysisContext : ParameterAnalysisContext
{
    public override string DefaultName { get; }

    public override TypeAnalysisContext DefaultParameterType { get; }

    public override ParameterAttributes DefaultAttributes { get; }

    public override ConstantValue? OriginalDefaultValue { get; }

    protected override bool IsInjected => true;

    public InjectedParameterAnalysisContext(string? name, TypeAnalysisContext typeContext, ParameterAttributes attributes, int parameterIndex, MethodAnalysisContext declaringMethod, ConstantValue? defaultValue = null) : base(null, parameterIndex, declaringMethod)
    {
        DefaultName = name ?? $"param_{parameterIndex}";
        DefaultParameterType = typeContext;
        DefaultAttributes = attributes;
        OriginalDefaultValue = defaultValue;
    }
}
