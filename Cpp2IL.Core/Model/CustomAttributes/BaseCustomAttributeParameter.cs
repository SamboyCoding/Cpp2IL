using System.IO;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;

public abstract class BaseCustomAttributeParameter(
    AnalyzedCustomAttribute owner,
    CustomAttributeParameterKind kind,
    int index)
{
    public AnalyzedCustomAttribute Owner { get; } = owner;
    public CustomAttributeParameterKind Kind { get; } = kind;
    public int Index { get; } = index;

    public abstract void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context);
}
