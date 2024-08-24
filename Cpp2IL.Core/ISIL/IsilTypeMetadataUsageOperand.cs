using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ISIL;

public readonly struct IsilTypeMetadataUsageOperand(TypeAnalysisContext typeAnalysisContext) : IsilOperandData
{
    public readonly TypeAnalysisContext TypeAnalysisContext = typeAnalysisContext;

    public override string ToString() => "typeof(" + TypeAnalysisContext.FullName + ")";
}
