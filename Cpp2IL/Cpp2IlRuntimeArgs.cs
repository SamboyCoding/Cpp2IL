using Cpp2IL.Core;

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

        public string AssemblyToRunAnalysisFor;
        public bool AnalyzeAllAssemblies;
        
        //Feature flags
        public bool EnableAnalysis;
        public bool EnableMetadataGeneration;
        public bool EnableRegistrationPrompts;
        public bool EnableIlToAsm;
        public bool IlToAsmContinueThroughErrors;
        public bool SuppressAttributes;
        public bool Parallel;

        public bool EnableVerboseLogging;

        public AnalysisLevel AnalysisLevel;

        public string OutputRootDirectory;
    }
}