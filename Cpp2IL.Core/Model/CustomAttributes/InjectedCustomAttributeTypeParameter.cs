using System.IO;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;

/// <summary>
/// Represents an injected <see cref="BaseCustomAttributeTypeParameter"/> for a <see cref="TypeAnalysisContext"/>.
/// </summary>
public class InjectedCustomAttributeTypeParameter : BaseCustomAttributeTypeParameter
{
    public override TypeAnalysisContext? TypeContext { get; }

    public InjectedCustomAttributeTypeParameter(TypeAnalysisContext? type, AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index) : base(owner, kind, index)
    {
        TypeContext = type;
    }

    public override void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context) => throw new System.NotSupportedException();
}
