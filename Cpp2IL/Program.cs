using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CommandLine;
using Cpp2IL.Core;
using Cpp2IL.Core.Exceptions;
using LibCpp2IL;
using LibCpp2IL.PE;

namespace Cpp2IL
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    internal class Program
    {
        private static readonly string[] BlacklistedExecutableFilenames =
        {
            "UnityCrashHandler.exe",
            "UnityCrashHandler64.exe",
            "install.exe",
            "launch.exe",
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

                result.UnityVersion = Cpp2IlApi.DetermineUnityVersion(unityPlayerPath, Path.Combine(baseGamePath, $"{exeName}_Data"));

                Logger.InfoNewline($"Determined game's unity version to be {string.Join(".", result.UnityVersion)}");

                if (result.UnityVersion[0] <= 4)
                    throw new SoftException($"Unable to determine a valid unity version (got {result.UnityVersion.ToStringEnumerable()})");

                result.Valid = true;
            }
            else
            {
                Logger.WarnNewline("Using force options, I sure hope you know what you're doing!");
                result.PathToAssembly = options.ForcedBinaryPath!;
                result.PathToMetadata = options.ForcedMetadataPath!;
                result.UnityVersion = options.ForcedUnityVersion!.Split('.').Select(int.Parse).ToArray();
                result.Valid = true;
            }

            result.EnableAnalysis = !options.SkipAnalysis;
            result.EnableMetadataGeneration = !options.SkipMetadataTextFiles;
            result.EnableRegistrationPrompts = !options.DisableRegistrationPrompts;
            result.EnableVerboseLogging = options.Verbose;

            result.AnalysisLevel = (AnalysisLevel) options.AnalysisLevel;
            
            result.OutputRootDirectory = Path.GetFullPath("cpp2il_out");

            return result;
        }

        public static int Main(string[] args)
        {
            Console.WriteLine("===Cpp2IL by Samboy063===");
            Console.WriteLine("A Tool to Reverse Unity's \"il2cpp\" Build Process.\n");

            Logger.CheckColorSupport();
            
            Logger.InfoNewline("Running on " + Environment.OSVersion.Platform);

            try
            {
                var runtimeArgs = GetRuntimeOptionsFromCommandLine(args);

                return MainWithArgs(runtimeArgs);
            }
            catch (DllSaveException e)
            {
                Logger.ErrorNewline(e.ToString());
                Console.WriteLine();
                Console.WriteLine();
                Logger.ErrorNewline("Waiting for you to press enter - feel free to copy the error...");
                Console.ReadLine();

                return -1;
            }
            catch (LibCpp2ILInitializationException e)
            {
                Logger.ErrorNewline($"\n\n{e}\n\nWaiting for you to press enter - feel free to copy the error...");
                Console.ReadLine();
                return -1;
            }
            catch (SoftException e)
            {
                Logger.ErrorNewline($"Execution Failed: {e.Message}");
                return -1;
            }
        }

        public static int MainWithArgs(Cpp2IlRuntimeArgs runtimeArgs)
        {
            if (!runtimeArgs.Valid)
                throw new SoftException("Arguments have Valid = false");

            Cpp2IlApi.InitializeLibCpp2Il(runtimeArgs.PathToAssembly, runtimeArgs.PathToMetadata, runtimeArgs.UnityVersion, runtimeArgs.EnableVerboseLogging, runtimeArgs.EnableRegistrationPrompts);

            Cpp2IlApi.MakeDummyDLLs();

            if (runtimeArgs.EnableMetadataGeneration)
                Cpp2IlApi.GenerateMetadataForAllAssemblies(runtimeArgs.OutputRootDirectory);

            KeyFunctionAddresses? keyFunctionAddresses = null;

            //We have to always run key function scan (if we can), so that attribute reconstruction can run.
            if (LibCpp2IlMain.Binary?.InstructionSet == InstructionSet.X86_32 || LibCpp2IlMain.Binary?.InstructionSet == InstructionSet.X86_64 && LibCpp2IlMain.Binary is PE)
            {
                Logger.InfoNewline("Running Scan for Known Functions...");

                //This part involves decompiling known functions to search for other function calls
                keyFunctionAddresses = Cpp2IlApi.ScanForKeyFunctionAddresses();
            }

            Logger.InfoNewline("Applying type, method, and field attributes...This may take a couple of seconds");
            var start = DateTime.Now;

            Cpp2IlApi.RunAttributeRestorationForAllAssemblies(keyFunctionAddresses);

            Logger.InfoNewline($"Finished Applying Attributes in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

            if (runtimeArgs.EnableAnalysis) 
                Cpp2IlApi.PopulateConcreteImplementations();

            Cpp2IlApi.SaveAssemblies(runtimeArgs.OutputRootDirectory);

            if (runtimeArgs.EnableAnalysis) 
                DoAssemblyCSharpAnalysis(runtimeArgs.AnalysisLevel, runtimeArgs.OutputRootDirectory, keyFunctionAddresses!);

            Logger.InfoNewline("Done.");
            return 0;
        }

        private static void DoAssemblyCSharpAnalysis(AnalysisLevel analysisLevel, string rootDir, KeyFunctionAddresses keyFunctionAddresses)
        {
            var assemblyCsharp = Cpp2IlApi.GetAssemblyByName("Assembly-CSharp");

            if (assemblyCsharp == null)
                return;

            Cpp2IlApi.AnalyseAssembly(analysisLevel, assemblyCsharp, keyFunctionAddresses, Path.Combine(rootDir, "types"), false);
        }
    }
}