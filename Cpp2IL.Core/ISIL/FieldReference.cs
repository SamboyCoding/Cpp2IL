using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ISIL;

public class FieldReference(FieldAnalysisContext field, LocalVariable local, int offset)
{
    public FieldAnalysisContext Field = field;
    public LocalVariable Local = local;
    public int Offset = offset;

    public override string ToString() => $"{Local.Name}.{Field.Name} ({Field.FieldType.FullName})";
}
