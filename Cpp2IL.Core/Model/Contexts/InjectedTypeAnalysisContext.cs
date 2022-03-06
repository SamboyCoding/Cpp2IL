using System.Linq;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedTypeAnalysisContext : TypeAnalysisContext
{
    public override string DefaultName { get; }

    public override string DefaultNs { get; }

    public InjectedTypeAnalysisContext(AssemblyAnalysisContext parent, string name, string ns) : base(null, parent)
    {
        DefaultName = name;
        DefaultNs = ns;
    }

    public void InjectMethodContext(string methodName, bool isStatic, TypeAnalysisContext returnType, params TypeAnalysisContext[] args)
    {
        if (args.Any(a => a.Definition == null))
            throw new("Cannot inject a method using injected types as parameters, yet.");

        var method = new InjectedMethodAnalysisContext(this, methodName, isStatic, returnType, args);
        Methods.Add(method);
    }
}