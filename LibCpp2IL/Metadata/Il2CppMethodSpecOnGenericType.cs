using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata
{
    public class Il2CppMethodSpecOnGenericType : ReadableClass
    {
        public Il2CppVariableWidthIndex<Il2CppMethodDefinition> methodDefinitionIndex;
        public Il2CppVariableWidthIndex<Il2CppGenericInst> classIndexIndex;

        public Il2CppGenericInst GenericClassInst => OwningContext.Binary.GetGenericInst(classIndexIndex);
        public Il2CppMethodDefinition MethodDefinition => OwningContext.Metadata.GetMethodDefinitionFromIndex(methodDefinitionIndex);
        
        public override void Read(ClassReadingBinaryReader reader)
        {
            methodDefinitionIndex = Il2CppVariableWidthIndex<Il2CppMethodDefinition>.Read(reader);
            classIndexIndex = Il2CppVariableWidthIndex<Il2CppGenericInst>.Read(reader);
        }
    }
}
