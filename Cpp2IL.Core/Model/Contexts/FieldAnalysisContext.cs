using LibCpp2IL.Metadata;

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
    public readonly Il2CppFieldDefinition Definition;

    public FieldAnalysisContext(Il2CppFieldDefinition definition, TypeAnalysisContext parent) : base(parent.AppContext)
    {
        DeclaringType = parent;
        Definition = definition;
    }
}