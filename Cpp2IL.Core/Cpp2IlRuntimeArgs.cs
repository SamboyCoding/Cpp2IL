using System.Collections.Generic;
using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Api;

namespace Cpp2IL.Core
{
    public class Cpp2IlRuntimeArgs
    {
        //To determine easily if this struct is the default one or not.
        public bool Valid;

        //Core variables
        public UnityVersion UnityVersion;
        public byte[] Assembly = null!;
        public byte[] Metadata = null!;

        public string? WasmFrameworkJsFile;

        public List<Cpp2IlProcessingLayer> ProcessingLayersToRun = new();
        public readonly Dictionary<string, string> ProcessingLayerConfigurationOptions = new();

        public Cpp2IlOutputFormat? OutputFormat;
        public string OutputRootDirectory = null!;
    }
}