using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class EventAnalysisContext : HasCustomAttributes
{
    public readonly TypeAnalysisContext DeclaringType;
    public readonly Il2CppEventDefinition Definition;
    public readonly MethodAnalysisContext? Adder;
    public readonly MethodAnalysisContext? Remover;
    public readonly MethodAnalysisContext? Invoker;

    protected override int CustomAttributeIndex => Definition.customAttributeIndex;

    protected override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType.DeclaringAssembly;

    public EventAnalysisContext(Il2CppEventDefinition definition, TypeAnalysisContext parent) : base(definition.token, parent.AppContext)
    {
        Definition = definition;
        DeclaringType = parent;

        Adder = parent.GetMethod(definition.Adder);
        Remover = parent.GetMethod(definition.Remover);
        Invoker = parent.GetMethod(definition.Invoker);
    }
}