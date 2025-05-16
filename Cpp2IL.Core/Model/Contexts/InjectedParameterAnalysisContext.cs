using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedParameterAnalysisContext : ParameterAnalysisContext
{
    public override TypeAnalysisContext ParameterTypeContext { get; }

    public override ParameterAttributes ParameterAttributes { get; }
    
    protected override bool IsInjected => true;

    public InjectedParameterAnalysisContext(string? name, TypeAnalysisContext typeContext, ParameterAttributes attributes, int paramIndex, MethodAnalysisContext declaringMethod) : base(null, paramIndex, declaringMethod)
    {
        OverrideName = name ?? $"param_{paramIndex}";
        ParameterTypeContext = typeContext;
        ParameterAttributes = attributes;
    }
}
