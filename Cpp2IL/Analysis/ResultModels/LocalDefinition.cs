using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class LocalDefinition : IAnalysedOperand
    {
        public string Name;
        public TypeDefinition? Type;

        public override string ToString()
        {
            return $"{{'{Name}' (type {Type.FullName})}}";
        }
    }
}