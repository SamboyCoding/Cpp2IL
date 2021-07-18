using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class StaticFieldsPtr
    {
        public TypeReference TypeTheseFieldsAreFor;

        public StaticFieldsPtr(TypeReference typeTheseFieldsAreFor)
        {
            TypeTheseFieldsAreFor = typeTheseFieldsAreFor;
        }
    }
}