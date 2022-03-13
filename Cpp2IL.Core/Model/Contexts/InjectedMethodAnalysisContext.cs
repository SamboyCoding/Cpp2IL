using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedMethodAnalysisContext : MethodAnalysisContext
{
    public override string DefaultName { get; }

    public override bool IsStatic { get; }

    public override MethodAttributes Attributes { get; }

    public InjectedMethodAnalysisContext(TypeAnalysisContext parent, string name, bool isStatic, TypeAnalysisContext returnType, MethodAttributes attributes, TypeAnalysisContext[] injectedParameters) : base(null, parent)
    {
        DefaultName = name;
        InjectedReturnType = returnType;
        IsStatic = isStatic;
        Attributes = attributes;
        InjectedParameterTypes = injectedParameters;
    }
}