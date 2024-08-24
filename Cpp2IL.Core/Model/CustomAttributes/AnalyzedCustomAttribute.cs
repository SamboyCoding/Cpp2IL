using System.Collections.Generic;
using System.Text;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;

/// <summary>
/// A class which represents a managed custom attribute applied to some object (type, member, assembly).
/// </summary>
public class AnalyzedCustomAttribute(MethodAnalysisContext constructor)
{
    /// <summary>
    /// The constructor that is being used to create this custom attribute.
    /// </summary>
    public readonly MethodAnalysisContext Constructor = constructor;

    /// <summary>
    /// Any arguments that are passed to the constructor.
    /// </summary>
    public readonly List<BaseCustomAttributeParameter> ConstructorParameters = [];

    /// <summary>
    /// Any fields that are set on the custom attribute.
    /// </summary>
    public readonly List<CustomAttributeField> Fields = [];

    /// <summary>
    /// Any properties that are set on the custom attribute.
    /// </summary>
    public readonly List<CustomAttributeProperty> Properties = [];

    /// <summary>
    /// Returns true if this custom attribute's constructor has any parameters.
    /// </summary>
    public bool HasAnyParameters => Constructor.ParameterCount > 0;

    /// <summary>
    /// Returns true if either the constructor has no parameters or if all of the parameters are assigned values.
    /// </summary>
    public bool IsSuitableForEmission => !HasAnyParameters || ConstructorParameters.Count == Constructor.ParameterCount;

    public bool AnyFieldsOrPropsSet => Fields.Count + Properties.Count > 0;

    public override string ToString()
    {
        var sb = new StringBuilder("[");

        var attributeTypeName = Constructor.DeclaringType!.Name!;

        const string suffix = "Attribute";
        if (attributeTypeName.EndsWith(suffix))
            attributeTypeName = attributeTypeName[..^suffix.Length];

        sb.Append(attributeTypeName);

        if (HasAnyParameters || AnyFieldsOrPropsSet)
            sb.Append('(');

        if (!IsSuitableForEmission)
            sb.Append("/*Cpp2IL Warning: missing at least one required parameter*/");

        if (ConstructorParameters.Count + Fields.Count + Properties.Count > 0)
        {
            var needComma = false;

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

            foreach (var prop in Properties)
            {
                if (needComma)
                    sb.Append(", ");

                sb.Append(prop);
                needComma = true;
            }
        }

        if (HasAnyParameters || AnyFieldsOrPropsSet)
            sb.Append(')');

        sb.Append(']');
        return sb.ToString();
    }
}
