using System.Collections.Generic;
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

    public Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext> InjectMethodToAllAssemblies(string name, bool isStatic, TypeAnalysisContext returnType, MethodAttributes attributes, params TypeAnalysisContext[] args) 
        => InjectedTypes.ToDictionary(t => t.DeclaringAssembly, t => t.InjectMethodContext(name, isStatic, returnType, attributes, args));

    public Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext> InjectConstructor(bool isStatic, params TypeAnalysisContext[] args) 
        => InjectMethodToAllAssemblies(isStatic ? ".cctor" : ".ctor", isStatic, InjectedTypes.First().AppContext.SystemTypes.SystemVoidType, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, args);
    
    public Dictionary<AssemblyAnalysisContext, InjectedFieldAnalysisContext> InjectFieldToAllAssemblies(string name, TypeAnalysisContext fieldType, FieldAttributes attributes) 
        => InjectedTypes.ToDictionary(t => t.DeclaringAssembly, t => t.InjectFieldContext(name, fieldType, attributes));
}
