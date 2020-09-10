using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class NewSafeCastResult
    {
        public LocalDefinition original;
        public TypeDefinition castTo;
    }
}