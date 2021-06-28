namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppGenericMethodIndices
    {
        public int methodIndex;
        public int invokerIndex;
        
        //Present in v27.1 and v24.5, but not v27.0
        [Version(Min = 27.1f)]
        [Version(Min = 24.5f, Max = 24.5f)]
        public int adjustorThunk;
    }
}