using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class LocalDefinition : IAnalysedOperand
    {
        public string Name;
        public TypeReference? Type;
        public object? KnownInitialValue;

        public override string ToString()
        {
            return $"{{'{Name}' (type {Type?.FullName})}}";
        }

        public string GetPseudocodeRepresentation()
        {
            return Name;
        }
    }
}