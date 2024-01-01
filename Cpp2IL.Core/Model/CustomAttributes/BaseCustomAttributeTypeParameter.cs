using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;

/// <summary>
/// Represents a custom attribute parameter which is a type reference (typeof(x))
/// </summary>
public abstract class BaseCustomAttributeTypeParameter : BaseCustomAttributeParameter
{
    public abstract TypeAnalysisContext? TypeContext { get; }

    public BaseCustomAttributeTypeParameter(AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index) : base(owner, kind, index)
    {
    }
}
