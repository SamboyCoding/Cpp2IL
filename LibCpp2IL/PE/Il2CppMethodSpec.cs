using LibCpp2IL.Metadata;

namespace LibCpp2IL.PE
{
    public class Il2CppMethodSpec
    {
        public int methodDefinitionIndex;
        public int classIndexIndex;
        public int methodIndexIndex;

        public Il2CppMethodDefinition? MethodDefinition => LibCpp2IlMain.TheMetadata?.methodDefs[methodDefinitionIndex];

        public Il2CppGenericInst? GenericClassInst => LibCpp2IlMain.ThePe?.genericInsts[classIndexIndex];
        
        public Il2CppGenericInst? GenericMethodInst => LibCpp2IlMain.ThePe?.genericInsts[methodIndexIndex];
    };
}