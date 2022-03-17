using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using CommandLine;

namespace Cpp2IL
{
    [SuppressMessage("ReSharper", "NotNullMemberIsNotInitialized")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public class CommandLineArgs
    {
        [Option("game-path", HelpText = "Specify path to the game folder (containing the exe)")]
        public string GamePath { get; set; } = null!; //Suppressed because it's set by CommandLineParser.

        [Option("exe-name", HelpText = "Specify an override for the unity executable name in case the auto-detection doesn't work.")]
        public string? ExeName { get; set; }
        
        //Force options

        [Option("force-binary-path", HelpText = "Force the path to the il2cpp binary. Don't use unless you know what you're doing, and use in conjunction with the other force options.")]
        public string? ForcedBinaryPath { get; set; }
            
        [Option("force-metadata-path", HelpText = "Force the path to the il2cpp metadata file. Don't use unless you know what you're doing, and use in conjunction with the other force options.")]
        public string? ForcedMetadataPath { get; set; }
            
        [Option("force-unity-version", HelpText = "Override the unity version detection. Don't use unless you know what you're doing, and use in conjunction with the other force options.")]
        public string? ForcedUnityVersion { get; set; }
        
        //Processor options
        
        [Option("list-processors", HelpText = "List the available processing layers and exit.")]
        public bool ListProcessors { get; set; }
        
        [Option("use-processor", HelpText = "Specify the ID of a processing layer to use. This argument can appear more than once, in which case layers will be executed in the order they are specified.")]
        public IEnumerable<string> ProcessorsToUse { get; set; } = new List<string>();
        
        [Option("processor-config", HelpText = "Specify a configuration option for one of the processors you have selected to use, in the format key=value. This argument can appear more than once for specifying multiple keys. The configuration options are used as needed by the selected processors.")]
        public IEnumerable<string> ProcessorConfigOptions { get; set; } = new List<string>();
        
        //Output options
        
        [Option("list-output-formats", HelpText = "List the available output formats and exit.")]
        public bool ListOutputFormats { get; set; }
        
        //FUTURE: Allow multiple of these?
        [Option("output-as", HelpText = "Specify the ID of the output format you wish to use.")]
        public string? OutputFormatId { get; set; }
        
        [Option("output-to", HelpText = "Root directory to output to. Defaults to cpp2il_out in the current working directory.")]
        public string OutputRootDir { get; set; } = Path.GetFullPath("cpp2il_out");

        //Flags

        [Option("verbose", HelpText = "Enable Verbose Logging.")]
        public bool Verbose { get; set; }

        [Option("wasm-framework-file", HelpText = "Path to the wasm *.framework.js file. Only needed if your binary is a WASM file. If provided, it can be used to remap obfuscated dynCall function names in order to correct method pointers.")]
        public string? WasmFrameworkFilePath { get; set; }

        internal bool AreForceOptionsValid
        {
            get
            {
                if (ForcedBinaryPath != null && ForcedMetadataPath != null && ForcedUnityVersion != null)
                    return true;
                if (ForcedBinaryPath == null && ForcedMetadataPath == null && ForcedUnityVersion == null)
                    return true;

                return false;
            }
        }
    }
}