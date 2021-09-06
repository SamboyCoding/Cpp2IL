using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class NewSafeCastResult<T>
    {
        public LocalDefinition<T> original;
        public TypeReference castTo;

        public override string ToString()
        {
            return $"{{{original.Name} as? {castTo}}}";
        }
    }
}