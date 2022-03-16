using System.Collections.Generic;
using System.Text;
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
    public bool HasAnyParameters => Constructor.ParameterCount > 0;

    /// <summary>
    /// Returns true if either the constructor has no parameters or if all of the parameters are assigned values.
    /// </summary>
    public bool IsSuitableForEmission => !HasAnyParameters || ConstructorParameters.Count == Constructor.ParameterCount;

    public AnalyzedCustomAttribute(MethodAnalysisContext constructor)
    {
        Constructor = constructor;
    }

    public override string ToString()
    {
        var sb = new StringBuilder("[");

        var attributeTypeName = Constructor.Definition!.DeclaringType!.Name!;

        const string suffix = "Attribute";
        if(attributeTypeName.EndsWith(suffix))
            attributeTypeName = attributeTypeName[..^suffix.Length];
        
        sb.Append(attributeTypeName);
        
        if (ConstructorParameters.Count + Fields.Count + Properties.Count > 0)
        {
            var needComma = false;
            sb.Append('(');
            
            foreach (var param in ConstructorParameters)
            {
                if (needComma)
                    sb.Append(", ");
                
                sb.Append(param);
                needComma = true;
            }

            foreach (var field in Fields)
            {
                if (needComma)
                    sb.Append(", ");
                
                sb.Append(field);
                needComma = true;
            }
            
            foreach(var prop in Properties)
            {
                if (needComma)
                    sb.Append(", ");
                
                sb.Append(prop);
                needComma = true;
            }

            sb.Append(')');
        }

        sb.Append(']');
        return sb.ToString();
    }
}