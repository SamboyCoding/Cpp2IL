using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class PropertyAnalysisContext : HasCustomAttributes
{
    public Il2CppPropertyDefinition Definition;
    public MethodAnalysisContext Getter;
    public MethodAnalysisContext Setter;
}