using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class ComparisonDirectFieldAccess : IComparisonArgument
    {
        public LocalDefinition localAccessedOn;
        public FieldDefinition fieldAccessed;
        public string GetPseudocodeRepresentation()
        {
            return $"{localAccessedOn.Name}.{fieldAccessed.Name}";
        }
    }
}