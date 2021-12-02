using System.Collections.Generic;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;

/// <summary>
/// A class which represents a managed custom attribute applied to some object (type, member, assembly).
/// </summary>
public class AnalyzedCustomAttribute
{
    /// <summary>
    /// The constructor that is being used to create this custom attribute.
    /// </summary>
    public readonly MethodAnalysisContext Constructor;
    
    /// <summary>
    /// Any arguments that are passed to the constructor.
    /// </summary>
    public readonly List<BaseCustomAttributeParameter> ConstructorParameters = new();

    /// <summary>
    /// Any fields that are set on the custom attribute.
    /// </summary>
    public readonly List<CustomAttributeField> Fields = new();
    
    /// <summary>
    /// Any properties that are set on the custom attribute.
    /// </summary>
    public readonly List<CustomAttributeProperty> Properties = new();

    /// <summary>
    /// Returns true if this custom attribute's constructor has any parameters.
    /// </summary>
    public bool HasAnyParameters => Constructor.Definition.Parameters!.Length > 0;

    /// <summary>
    /// Returns true if either the constructor has no parameters or if all of the parameters are assigned values.
    /// </summary>
    public bool IsSuitableForEmission => !HasAnyParameters || ConstructorParameters.Count == Constructor.Definition.Parameters!.Length;

    public AnalyzedCustomAttribute(MethodAnalysisContext constructor)
    {
        Constructor = constructor;
    }
}