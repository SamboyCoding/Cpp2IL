namespace Cpp2IL
{
    public struct Cpp2IlRuntimeArgs
    {
        //To determine easily if this struct is the default one or not.
        public bool Valid;
        
        //Core variables
        public int[] UnityVersion;
        public string PathToAssembly;
        public string PathToMetadata;
        
        //Feature flags
        public bool EnableAnalysis;
        public bool EnableMetadataGeneration;
        public bool EnableRegistrationPrompts;

        public EAnalysisLevel AnalysisLevel;

        public enum EAnalysisLevel
        {
            PRINT_ALL,
            SKIP_ASM,
            SKIP_ASM_AND_SYNOPSIS,
            IL_ONLY,
            PSUEDOCODE_ONLY
        }
    }
}