using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class EventAnalysisContext : HasCustomAttributesAndName
{
    public readonly TypeAnalysisContext DeclaringType;
    public readonly Il2CppEventDefinition Definition;
    public readonly MethodAnalysisContext? Adder;
    public readonly MethodAnalysisContext? Remover;
    public readonly MethodAnalysisContext? Invoker;

    protected override int CustomAttributeIndex => Definition.customAttributeIndex;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType.DeclaringAssembly;

    public override string DefaultName => Definition.Name!;

    public EventAnalysisContext(Il2CppEventDefinition definition, TypeAnalysisContext parent) : base(definition.token, parent.AppContext)
    {
        Definition = definition;
        DeclaringType = parent;
        
        InitCustomAttributeData();

        Adder = parent.GetMethod(definition.Adder);
        Remover = parent.GetMethod(definition.Remover);
        Invoker = parent.GetMethod(definition.Invoker);
    }
    
    public override string ToString() => $"Event: {Definition.DeclaringType!.Name}::{Definition.Name}";
}