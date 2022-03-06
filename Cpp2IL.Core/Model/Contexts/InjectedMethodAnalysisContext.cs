namespace Cpp2IL.Core.Model.Contexts;

public class InjectedMethodAnalysisContext : MethodAnalysisContext
{
    public override string DefaultName { get; }

    public override bool IsStatic { get; }

    public InjectedMethodAnalysisContext(TypeAnalysisContext parent, string name, bool isStatic, TypeAnalysisContext returnType, TypeAnalysisContext[] injectedParameters) : base(null, parent)
    {
        DefaultName = name;
        InjectedReturnType = returnType;
        IsStatic = isStatic;
        InjectedParameterTypes = injectedParameters;
    }
}