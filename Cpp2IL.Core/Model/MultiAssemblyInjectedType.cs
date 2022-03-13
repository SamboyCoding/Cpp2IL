using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model;

public class MultiAssemblyInjectedType
{
    public InjectedTypeAnalysisContext[] InjectedTypes { get; }
    
    public MultiAssemblyInjectedType(InjectedTypeAnalysisContext[] injectedTypes)
    {
        InjectedTypes = injectedTypes;
    }

    public void InjectMethod(string name, bool isStatic, TypeAnalysisContext returnType, MethodAttributes attributes, params TypeAnalysisContext[] args)
    {
        foreach (var injectedTypeAnalysisContext in InjectedTypes)
        {
            injectedTypeAnalysisContext.InjectMethodContext(name, isStatic, returnType, attributes, args);
        }
    }
    
    public void InjectConstructor(bool isStatic, params TypeAnalysisContext[] args) 
        => InjectMethod(isStatic ? ".ctor" : ".cctor", isStatic, InjectedTypes.First().AppContext.SystemTypes.SystemVoidType, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, args);
}