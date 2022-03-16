using System.Reflection;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedFieldAnalysisContext : FieldAnalysisContext
{
    public override Il2CppType FieldType { get; }
    public override FieldAttributes Attributes { get; }

    public InjectedFieldAnalysisContext(string name, TypeAnalysisContext type, FieldAttributes attributes, TypeAnalysisContext parent) : base(null, parent)
    {
        OverrideName = name;
        Attributes = attributes;
        FieldType = LibCpp2IlReflection.GetTypeFromDefinition(type.Definition ?? throw new("Fields with a type of an injected type are not supported yet."))!;
    }
}