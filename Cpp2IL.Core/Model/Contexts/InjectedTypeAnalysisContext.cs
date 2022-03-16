using System.Linq;
using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedTypeAnalysisContext : TypeAnalysisContext
{
    public override string DefaultName { get; }

    public override string DefaultNs { get; }

    public InjectedTypeAnalysisContext(AssemblyAnalysisContext containingAssembly, string name, string ns, TypeAnalysisContext? baseType) : base(null, containingAssembly)
    {
        DefaultName = name;
        DefaultNs = ns;
        OverrideBaseType = baseType;
    }

    public InjectedMethodAnalysisContext InjectMethodContext(string methodName, bool isStatic, TypeAnalysisContext returnType, MethodAttributes attributes, params TypeAnalysisContext[] args)
    {
        if (args.Any(a => a.Definition == null))
            throw new("Cannot inject a method using injected types as parameters, yet.");

        var method = new InjectedMethodAnalysisContext(this, methodName, isStatic, returnType, attributes, args);
        Methods.Add(method);

        return method;
    }

    public InjectedFieldAnalysisContext InjectFieldContext(string fieldName, TypeAnalysisContext fieldType, FieldAttributes attributes)
    {
        var field = new InjectedFieldAnalysisContext(fieldName, fieldType, attributes, this);
        Fields.Add(field);
        return field;
    }
}