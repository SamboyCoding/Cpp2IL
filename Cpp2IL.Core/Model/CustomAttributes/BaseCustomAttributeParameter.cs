using System.IO;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;

public abstract class BaseCustomAttributeParameter
{
    public AnalyzedCustomAttribute Owner { get; }
    public CustomAttributeParameterKind Kind { get; }
    public int Index { get; }

    protected BaseCustomAttributeParameter(AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index)
    {
        Owner = owner;
        Kind = kind;
        Index = index;
    }

    public abstract void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context);
}
