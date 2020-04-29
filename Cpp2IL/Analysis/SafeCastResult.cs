using Mono.Cecil;

namespace Cpp2IL
{
    public class SafeCastResult
    {
        public TypeDefinition originalType;
        public string originalAlias;
        public TypeDefinition castTo;
    }
}