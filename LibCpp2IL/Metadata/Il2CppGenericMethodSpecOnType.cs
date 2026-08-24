using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata
{
    public class Il2CppGenericMethodSpecOnType : ReadableClass
    {
        public Il2CppVariableWidthIndex<Il2CppMethodDefinition> methodDefinitionIndex;
        public Il2CppVariableWidthIndex<Il2CppGenericInst> methodIndexIndex;

        public Il2CppMethodDefinition MethodDefinition => OwningContext.Metadata.GetMethodDefinitionFromIndex(methodDefinitionIndex);
        public Il2CppGenericInst GenericMethodInst => OwningContext.Binary.GetGenericInst(methodIndexIndex);

        public override void Read(ClassReadingBinaryReader reader)
        {
            methodDefinitionIndex = Il2CppVariableWidthIndex<Il2CppMethodDefinition>.Read(reader);
            methodIndexIndex = Il2CppVariableWidthIndex<Il2CppGenericInst>.Read(reader);
        }
    }
}
