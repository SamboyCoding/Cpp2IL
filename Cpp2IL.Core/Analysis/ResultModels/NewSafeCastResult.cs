using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class NewSafeCastResult
    {
        public LocalDefinition original;
        public TypeReference castTo;

        public override string ToString()
        {
            return $"{{{original.Name} as? {castTo}}}";
        }
    }
}