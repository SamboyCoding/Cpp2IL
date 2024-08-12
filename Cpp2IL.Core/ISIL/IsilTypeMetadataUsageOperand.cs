using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ISIL
{
    public readonly struct IsilTypeMetadataUsageOperand : IsilOperandData
    {
        public readonly TypeAnalysisContext TypeAnalysisContext;

        public IsilTypeMetadataUsageOperand(TypeAnalysisContext typeAnalysisContext)
        {
            TypeAnalysisContext = typeAnalysisContext;
        }

        public override string ToString() => "typeof("+TypeAnalysisContext.FullName + ")";
    }
}
