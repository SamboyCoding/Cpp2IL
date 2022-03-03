using System.Collections.Generic;
using Cpp2IL.Core.Api;

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
        public string? WasmFrameworkJsFile;

        public List<Cpp2IlProcessingLayer> ProcessingLayersToRun = new();
        public Dictionary<string, string> ProcessingLayerConfigurationOptions = new();
        
        public Cpp2IlOutputFormat OutputFormat;
        public string OutputRootDirectory;
    }
}