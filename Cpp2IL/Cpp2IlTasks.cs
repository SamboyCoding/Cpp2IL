using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using CommandLine;
using LibCpp2IL;

namespace Cpp2IL
{
    public class Cpp2IlTasks
    {
        private static readonly string[] BlacklistedExecutableFilenames =
        {
            "UnityCrashHandler.exe",
            "UnityCrashHandler64.exe",
            "install.exe",
            "MelonLoader.Installer.exe"
        };
        
        public static Cpp2IlRuntimeArgs GetRuntimeOptionsFromCommandLine(string[] commandLine)
        {
            var parserResult = Parser.Default.ParseArguments<CommandLineArgs>(commandLine);

            if (!(parserResult is Parsed<CommandLineArgs> {Value: { } options}))
                throw new SoftException("Failed to parse command line arguments");

            if (!options.AreForceOptionsValid)
                throw new SoftException("Invalid force option configuration");

            var result = new Cpp2IlRuntimeArgs();
            
            if (options.ForcedBinaryPath == null)
            {
                var baseGamePath = options.GamePath;

                if (!Directory.Exists(baseGamePath))
                    throw new SoftException($"Specified game-path does not exist: {baseGamePath}");

                result.PathToAssembly = Path.Combine(baseGamePath, "GameAssembly.dll");
                var exeName = Path.GetFileNameWithoutExtension(Directory.GetFiles(baseGamePath)
                    .First(f => f.EndsWith(".exe") && !BlacklistedExecutableFilenames.Any(bl => f.EndsWith(bl))));

                exeName = options.ExeName ?? exeName;

                var unityPlayerPath = Path.Combine(baseGamePath, $"{exeName}.exe");
                result.PathToMetadata = Path.Combine(baseGamePath, $"{exeName}_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

                if (!File.Exists(result.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(result.PathToMetadata))
                    throw new SoftException("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
                                            $"\t{result.PathToAssembly}\n" +
                                            $"\t{unityPlayerPath}\n" +
                                            $"\t{result.PathToMetadata}\n");

                result.UnityVersion = DetermineUnityVersion(unityPlayerPath, Path.Combine(baseGamePath, $"{exeName}_Data"));

                if (result.UnityVersion[0] <= 4)
                    throw new SoftException($"Unable to determine a valid unity version (got {result.UnityVersion.ToStringEnumerable()})");

                result.Valid = true;
            }
            else
            {
                Console.WriteLine("Warning: Using force options, I sure hope you know what you're doing!");
                result.PathToAssembly = options.ForcedBinaryPath!;
                result.PathToMetadata = options.ForcedMetadataPath!;
                result.UnityVersion = options.ForcedUnityVersion!.Split('.').Select(int.Parse).ToArray();
                result.Valid = true;
            }

            result.EnableAnalysis = !options.SkipAnalysis;
            result.EnableMetadataGeneration = !options.SkipMetadataTextFiles;
            result.EnableRegistrationPrompts = !options.DisableRegistrationPrompts;

            return result;
        }

        private static int[] DetermineUnityVersion(string unityPlayerPath, string gameDataPath)
        {
            int[] version;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var unityVer = FileVersionInfo.GetVersionInfo(unityPlayerPath);

                version = new[] {unityVer.FileMajorPart, unityVer.FileMinorPart, unityVer.FileBuildPart};
            }
            else
            {
                //Globalgamemanagers
                var globalgamemanagersPath = Path.Combine(gameDataPath, "globalgamemanagers");
                var ggmBytes = File.ReadAllBytes(globalgamemanagersPath);
                var verString = new StringBuilder();
                var idx = 0x14;
                while (ggmBytes[idx] != 0)
                {
                    verString.Append(Convert.ToChar(ggmBytes[idx]));
                    idx++;
                }

                var unityVer = verString.ToString();
                unityVer = unityVer.Substring(0, unityVer.IndexOf("f", StringComparison.Ordinal));
                version = unityVer.Split(".").Select(int.Parse).ToArray();
            }

            return version;
        }

        public static void InitializeLibCpp2Il(Cpp2IlRuntimeArgs runtimeArgs)
        {
            //Set this flag from command line options
            LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput = runtimeArgs.EnableRegistrationPrompts;
            
            //Disable Method Ptr Mapping and Global Resolving if skipping analysis
            LibCpp2IlMain.Settings.DisableMethodPointerMapping = LibCpp2IlMain.Settings.DisableGlobalResolving = runtimeArgs.EnableAnalysis;

            if (!LibCpp2IlMain.LoadFromFile(runtimeArgs.PathToAssembly, runtimeArgs.PathToMetadata, runtimeArgs.UnityVersion))
                throw new SoftException("Initialization with LibCpp2Il failed");
        }
        
    }
}