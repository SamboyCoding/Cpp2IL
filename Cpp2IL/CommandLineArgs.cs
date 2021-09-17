using System.Diagnostics.CodeAnalysis;
using System.IO;
using CommandLine;

namespace Cpp2IL
{
    [SuppressMessage("ReSharper", "NotNullMemberIsNotInitialized")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public class CommandLineArgs
    {
        [Option("game-path", Required = true, HelpText = "Specify path to the game folder (containing the exe)")]
        public string GamePath { get; set; } = null!; //Suppressed because it's set by CommandLineParser.

        [Option("exe-name", Required = false, HelpText = "Specify an override for the unity executable name in case the auto-detection doesn't work.")]
        public string? ExeName { get; set; }
        
        [Option("analysis-level", Required = false, HelpText = "Specify a detail level for analysis. 0 prints everything and is the default. 1 omits the ASM, but still prints textual analysis, pseudocode, and IL. 2 omits ASM and textual analysis, but prints pseudocode and IL. 3 only prints IL. 4 only prints pseudocode.")]
        public int AnalysisLevel { get; set; }

        [Option("skip-analysis", Required = false, HelpText = "Skip the analysis section and stop once DummyDLLs have been generated.")]
        public bool SkipAnalysis { get; set; }

        [Option("skip-metadata-txts", Required = false, HelpText = "Skip the generation of [classname]_metadata.txt files.")]
        public bool SkipMetadataTextFiles { get; set; }

        [Option("disable-registration-prompts", Required = false, HelpText = "Disable the prompt if Code or Metadata Registration function addresses cannot be located.")]
        public bool DisableRegistrationPrompts { get; set; }
            
        [Option("force-binary-path", Required = false, HelpText = "Force the path to the il2cpp binary. Don't use unless you know what you're doing, and use in conjunction with the other force options.")]
        public string? ForcedBinaryPath { get; set; }
            
        [Option("force-metadata-path", Required = false, HelpText = "Force the path to the il2cpp metadata file. Don't use unless you know what you're doing, and use in conjunction with the other force options.")]
        public string? ForcedMetadataPath { get; set; }
            
        [Option("force-unity-version", Required = false, HelpText = "Override the unity version detection. Don't use unless you know what you're doing, and use in conjunction with the other force options.")]
        public string? ForcedUnityVersion { get; set; }

        [Option("verbose", Required = false, HelpText = "Enable Verbose Logging.")]
        public bool Verbose { get; set; }
        
        [Option("experimental-enable-il-to-assembly-please")]
        public bool EnableIlToAsm { get; set; }
        
        [Option("suppress-attributes", HelpText = "Suppress generation of Cpp2ILInjected attributes.")]
        public bool SuppressAttributes { get; set; }

        [Option("run-analysis-for-assembly", HelpText = "Specify the name of the assembly (without .dll) to run analysis for.")]
        public string RunAnalysisForAssembly { get; set; } = "Assembly-CSharp";

        [Option("parallel", HelpText = "Run analysis in parallel. Might break things.")]
        public bool Parallel { get; set; }

        [Option("output-root", HelpText = "Root directory to output to. Defaults to cpp2il_out in the current working directory.")]
        public string OutputRootDir { get; set; } = Path.GetFullPath("cpp2il_out");

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