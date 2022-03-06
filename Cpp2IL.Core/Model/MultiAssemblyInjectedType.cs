using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model;

public class MultiAssemblyInjectedType
{
    public InjectedTypeAnalysisContext[] InjectedTypes { get; }
    
    public MultiAssemblyInjectedType(InjectedTypeAnalysisContext[] injectedTypes)
    {
        InjectedTypes = injectedTypes;
    }

    public void InjectMethod(string name, bool isStatic, TypeAnalysisContext returnType, params TypeAnalysisContext[] args)
    {
        foreach (var injectedTypeAnalysisContext in InjectedTypes)
        {
            injectedTypeAnalysisContext.InjectMethodContext(name, isStatic, returnType, args);
        }
    }
}