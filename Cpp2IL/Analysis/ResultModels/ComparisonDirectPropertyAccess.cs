using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class ComparisonDirectPropertyAccess : IComparisonArgument
    {
        public LocalDefinition localAccessedOn;
        public PropertyDefinition propertyAccessed;

        public override string ToString()
        {
            return $"{{Property {propertyAccessed} on {localAccessedOn}}}";
        }
        
        public string GetPseudocodeRepresentation()
        {
            return $"{localAccessedOn.Name}.{propertyAccessed.Name}";
        }
    }
}