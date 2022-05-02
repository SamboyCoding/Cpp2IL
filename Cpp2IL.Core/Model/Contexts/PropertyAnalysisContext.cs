using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class PropertyAnalysisContext : HasCustomAttributesAndName
{
    public readonly TypeAnalysisContext DeclaringType;
    public readonly Il2CppPropertyDefinition Definition;
    
    public readonly MethodAnalysisContext? Getter;
    public readonly MethodAnalysisContext? Setter;
    
    protected override int CustomAttributeIndex => Definition.customAttributeIndex;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType.DeclaringAssembly;

    public override string DefaultName => Definition.Name!;

    public PropertyAnalysisContext(Il2CppPropertyDefinition definition, TypeAnalysisContext parent) : base(definition.token, parent.AppContext)
    {
        DeclaringType = parent;
        Definition = definition;
        
        InitCustomAttributeData();

        Getter = parent.GetMethod(definition.Getter);
        Setter = parent.GetMethod(definition.Setter);
    }
    
    public override string ToString() => $"Property:  {Definition.DeclaringType!.Name}::{Definition.Name}";
}