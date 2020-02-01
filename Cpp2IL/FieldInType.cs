using Mono.Cecil;

namespace Cpp2IL
{
    public struct FieldInType
    {
        public string Name;
        public TypeReference Type;
        public ulong Offset;
        public bool Static;
        public object Constant;
    }
}