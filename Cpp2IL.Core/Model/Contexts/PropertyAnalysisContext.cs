using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class PropertyAnalysisContext : HasCustomAttributes
{
    public readonly TypeAnalysisContext DeclaringType;
    public readonly Il2CppPropertyDefinition Definition;
    
    public readonly MethodAnalysisContext? Getter;
    public readonly MethodAnalysisContext? Setter;
    
    public PropertyAnalysisContext(Il2CppPropertyDefinition definition, TypeAnalysisContext parent) : base(parent.AppContext)
    {
        DeclaringType = parent;
        Definition = definition;

        Getter = parent.GetMethod(definition.Getter);
        Setter = parent.GetMethod(definition.Setter);
    }
}