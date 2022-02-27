using LibCpp2IL;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents a field in a managed type.
/// </summary>
public class FieldAnalysisContext : HasCustomAttributes
{
    /// <summary>
    /// The analysis context for the type that this field belongs to.
    /// </summary>
    public readonly TypeAnalysisContext DeclaringType;
    
    /// <summary>
    /// The underlying field metadata.
    /// </summary>
    public readonly Il2CppFieldReflectionData BackingData;
    
    protected override int CustomAttributeIndex => BackingData.field.customAttributeIndex;

    protected override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType.DeclaringAssembly;

    public override string CustomAttributeOwnerName => BackingData.field.Name!;

    public FieldAnalysisContext(Il2CppFieldReflectionData backingData, TypeAnalysisContext parent) : base(backingData.field.token, parent.AppContext)
    {
        DeclaringType = parent;
        BackingData = backingData;
        
        InitCustomAttributeData();
    }
    
    public override string ToString() => $"Field: {DeclaringType.Definition.Name}::{BackingData.field.Name}";
}