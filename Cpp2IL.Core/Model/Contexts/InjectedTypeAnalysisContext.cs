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
}