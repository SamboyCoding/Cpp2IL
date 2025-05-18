using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedFieldAnalysisContext : FieldAnalysisContext
{
    public override TypeAnalysisContext DefaultFieldType { get; }
    public override FieldAttributes Attributes { get; }

    protected override bool IsInjected => true;

    public InjectedFieldAnalysisContext(string name, TypeAnalysisContext type, FieldAttributes attributes, TypeAnalysisContext parent) : base(null, parent)
    {
        OverrideName = name;
        Attributes = attributes;
        DefaultFieldType = type;
    }
}
