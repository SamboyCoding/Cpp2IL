namespace Cpp2IL.Core
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

        public bool EnableVerboseLogging;

        public AnalysisLevel AnalysisLevel;

        public string OutputRootDirectory;
    }
}