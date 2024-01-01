using System.Reflection;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedFieldAnalysisContext : FieldAnalysisContext
{
    public override TypeAnalysisContext FieldTypeContext { get; }
    public override FieldAttributes Attributes { get; }

    public InjectedFieldAnalysisContext(string name, TypeAnalysisContext type, FieldAttributes attributes, TypeAnalysisContext parent) : base(null, parent)
    {
        OverrideName = name;
        Attributes = attributes;
        FieldTypeContext = type;
    }
}
