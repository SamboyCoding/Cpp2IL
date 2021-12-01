using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class EventAnalysisContext : HasCustomAttributes
{
    public Il2CppEventDefinition Definition;
    public MethodAnalysisContext Adder;
    public MethodAnalysisContext Remover;
    public MethodAnalysisContext Invoker;
}