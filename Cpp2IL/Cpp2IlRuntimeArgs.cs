using System.Collections.Generic;
using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Api;

namespace Cpp2IL
{
    public class Cpp2IlRuntimeArgs
    {
        //To determine easily if this struct is the default one or not.
        public bool Valid;
        
        //Core variables
        public UnityVersion UnityVersion;
        public string PathToAssembly;
        public string PathToMetadata;

        public string? WasmFrameworkJsFile;

        public List<Cpp2IlProcessingLayer> ProcessingLayersToRun = new();
        public Dictionary<string, string> ProcessingLayerConfigurationOptions = new();
        
        public Cpp2IlOutputFormat? OutputFormat;
        public string OutputRootDirectory;
    }
}