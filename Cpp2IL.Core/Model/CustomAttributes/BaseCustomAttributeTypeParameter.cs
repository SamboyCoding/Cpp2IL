using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;

/// <summary>
/// Represents a custom attribute parameter which is a type reference (typeof(x))
/// </summary>
public abstract class BaseCustomAttributeTypeParameter(
    AnalyzedCustomAttribute owner,
    CustomAttributeParameterKind kind,
    int index)
    : BaseCustomAttributeParameter(owner, kind, index)
{
    public abstract TypeAnalysisContext? TypeContext { get; }
}
