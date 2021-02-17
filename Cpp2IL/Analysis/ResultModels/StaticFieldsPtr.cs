using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
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