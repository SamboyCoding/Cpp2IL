using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedParameterAnalysisContext : ParameterAnalysisContext
{
    public override string DefaultName { get; }

    public override TypeAnalysisContext DefaultParameterType { get; }

    public override ParameterAttributes ParameterAttributes { get; }
    
    protected override bool IsInjected => true;

    public InjectedParameterAnalysisContext(string? name, TypeAnalysisContext typeContext, ParameterAttributes attributes, int paramIndex, MethodAnalysisContext declaringMethod) : base(null, paramIndex, declaringMethod)
    {
        DefaultName = name ?? $"param_{paramIndex}";
        DefaultParameterType = typeContext;
        ParameterAttributes = attributes;
    }
}
