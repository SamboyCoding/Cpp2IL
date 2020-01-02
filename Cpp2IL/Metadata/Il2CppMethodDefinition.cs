namespace Cpp2IL.Metadata
{
    public class Il2CppMethodDefinition
    {
        public int nameIndex;
        public int declaringType;
        public int returnType;
        public int parameterStart;
        [Version(Max = 24)] public int customAttributeIndex;
        public int genericContainerIndex;
        [Version(Max = 24.1f)] public int methodIndex;
        [Version(Max = 24.1f)] public int invokerIndex;
        [Version(Max = 24.1f)] public int delegateWrapperIndex;
        [Version(Max = 24.1f)] public int rgctxStartIndex;
        [Version(Max = 24.1f)] public int rgctxCount;
        public uint token;
        public ushort flags;
        public ushort iflags;
        public ushort slot;
        public ushort parameterCount;
    }
}