using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class StaticFieldsPtr
    {
        public TypeDefinition TypeTheseFieldsAreFor;

        public StaticFieldsPtr(TypeDefinition typeTheseFieldsAreFor)
        {
            TypeTheseFieldsAreFor = typeTheseFieldsAreFor;
        }
    }
}