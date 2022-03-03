using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime;
using CommandLine;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
#if !DEBUG
using Cpp2IL.Core.Exceptions;
#endif
using LibCpp2IL;
using LibCpp2IL.Wasm;

namespace Cpp2IL
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    internal class Program
    {
        private static readonly List<string> PathsToDeleteOnExit = new();

        private static void ResolvePathsFromCommandLine(string gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
        {
            if(string.IsNullOrEmpty(gamePath))
                throw new SoftException("No force options provided, and no game path was provided either. Please provide a game path or use the --force- options.");
            
            if (Directory.Exists(gamePath))
            {
                //Windows game.
                args.PathToAssembly = Path.Combine(gamePath, "GameAssembly.dll");
                var exeName = Path.GetFileNameWithoutExtension(Directory.GetFiles(gamePath)
                    .FirstOrDefault(f => f.EndsWith(".exe") && !MiscUtils.BlacklistedExecutableFilenames.Any(f.EndsWith)));

                exeName = inputExeName ?? exeName;

                if (exeName == null)
                    throw new SoftException("Failed to locate any executable in the provided game directory. Make sure the path is correct, and if you *really* know what you're doing (and know it's not supported), use the force options, documented if you provide --help.");

                var unityPlayerPath = Path.Combine(gamePath, $"{exeName}.exe");
                args.PathToMetadata = Path.Combine(gamePath, $"{exeName}_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

                if (!File.Exists(args.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(args.PathToMetadata))
                    throw new SoftException("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
                                            $"\t{args.PathToAssembly}\n" +
                                            $"\t{unityPlayerPath}\n" +
                                            $"\t{args.PathToMetadata}\n");

                Logger.VerboseNewline($"Found probable windows game at path: {gamePath}. Attempting to get unity version...");
                var gameDataPath = Path.Combine(gamePath, $"{exeName}_Data");
                var uv = Cpp2IlApi.DetermineUnityVersion(unityPlayerPath, gameDataPath);
                Logger.VerboseNewline($"First-attempt unity version detection gave: {uv?.ToString() ?? "null"}");

                if (uv == null)
                {
                    Logger.Warn("Could not determine unity version, probably due to not running on windows and not having any assets files to determine it from. Enter unity version, if known, in the format of (xxxx.x.x), else nothing to fail: ");
                    var userInputUv = Console.ReadLine();
                    uv = userInputUv?.Split('.').Select(int.Parse).ToArray();

                    if (uv == null)
                        throw new SoftException("Failed to determine unity version. If you're not running on windows, I need a globalgamemanagers file or a data.unity3d file, or you need to use the force options.");
                }

                args.UnityVersion = uv;

                if (args.UnityVersion[0] < 4)
                {
                    Logger.WarnNewline($"Fail once: Unity version of provided executable is {args.UnityVersion.ToStringEnumerable()}. This is probably not the correct version. Retrying with alternative method...");

                    var readUnityVersionFrom = Path.Combine(gameDataPath, "globalgamemanagers");
                    if (File.Exists(readUnityVersionFrom))
                        args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(File.ReadAllBytes(readUnityVersionFrom));
                    else
                    {
                        readUnityVersionFrom = Path.Combine(gameDataPath, "data.unity3d");
                        using var stream = File.OpenRead(readUnityVersionFrom);

                        args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(stream);
                    }
                }

                Logger.InfoNewline($"Determined game's unity version to be {string.Join(".", args.UnityVersion)}");

                if (args.UnityVersion[0] <= 4)
                    throw new SoftException($"Unable to determine a valid unity version (got {args.UnityVersion.ToStringEnumerable()})");

                args.Valid = true;
            }
            else if (File.Exists(gamePath) && Path.GetExtension(gamePath).ToLowerInvariant() == ".apk")
            {
                //APK
                //Metadata: assets/bin/Data/Managed/Metadata
                //Binary: lib/(armeabi-v7a)|(arm64-v8a)/libil2cpp.so

                Logger.InfoNewline($"Attempting to extract required files from APK {gamePath}", "APK");

                using var stream = File.OpenRead(gamePath);
                using var zipArchive = new ZipArchive(stream);

                var globalMetadata = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("assets/bin/Data/Managed/Metadata/global-metadata.dat"));
                var binary = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("lib/x86/libil2cpp.so"));
                binary ??= zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("lib/arm64-v8a/libil2cpp.so"));
                binary ??= zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("lib/armeabi-v7a/libil2cpp.so"));

                var globalgamemanagers = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("assets/bin/Data/globalgamemanagers"));
                var dataUnity3d = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("assets/bin/Data/data.unity3d"));

                if (binary == null)
                    throw new SoftException("Could not find libil2cpp.so inside the apk.");
                if (globalMetadata == null)
                    throw new SoftException("Could not find global-metadata.dat inside the apk");
                if (globalgamemanagers == null && dataUnity3d == null)
                    throw new SoftException("Could not find globalgamemanagers or data.unity3d inside the apk");

                var tempFileBinary = Path.GetTempFileName();
                var tempFileMeta = Path.GetTempFileName();

                PathsToDeleteOnExit.Add(tempFileBinary);
                PathsToDeleteOnExit.Add(tempFileMeta);

                Logger.InfoNewline($"Extracting APK/{binary.FullName} to {tempFileBinary}", "APK");
                binary.ExtractToFile(tempFileBinary, true);
                Logger.InfoNewline($"Extracting APK/{globalMetadata.FullName} to {tempFileMeta}", "APK");
                globalMetadata.ExtractToFile(tempFileMeta, true);

                args.PathToAssembly = tempFileBinary;
                args.PathToMetadata = tempFileMeta;

                if (globalgamemanagers != null)
                {
                    Logger.InfoNewline("Reading globalgamemanagers to determine unity version...", "APK");
                    var ggmBytes = new byte[0x40];
                    using var ggmStream = globalgamemanagers.Open();
                    ggmStream.Read(ggmBytes, 0, 0x40);

                    args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(ggmBytes);
                }
                else
                {
                    Logger.InfoNewline("Reading data.unity3d to determine unity version...", "APK");
                    using var du3dStream = dataUnity3d!.Open();

                    args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(du3dStream);
                }

                Logger.InfoNewline($"Determined game's unity version to be {string.Join(".", args.UnityVersion)}", "APK");

                args.Valid = true;
            }
            else
            {
                throw new SoftException($"Could not find a valid unity game at {gamePath}");
            }
        }

        private static Cpp2IlRuntimeArgs GetRuntimeOptionsFromCommandLine(string[] commandLine)
        {
            var parserResult = Parser.Default.ParseArguments<CommandLineArgs>(commandLine);

            if (parserResult is NotParsed<CommandLineArgs> notParsed && notParsed.Errors.Count() == 1 && notParsed.Errors.All(e => e.Tag is ErrorType.VersionRequestedError or ErrorType.HelpRequestedError))
                //Version or help requested
                Environment.Exit(0);

            if (parserResult is not Parsed<CommandLineArgs> {Value: { } options})
                throw new SoftException("Failed to parse command line arguments");

            if (!options.AreForceOptionsValid)
                throw new SoftException("Invalid force option configuration");

            var result = new Cpp2IlRuntimeArgs();

            if (options.ForcedBinaryPath == null)
            {
                ResolvePathsFromCommandLine(options.GamePath, options.ExeName, ref result);
            }
            else
            {
                Logger.WarnNewline("Using force options, I sure hope you know what you're doing!");
                result.PathToAssembly = options.ForcedBinaryPath!;
                result.PathToMetadata = options.ForcedMetadataPath!;
                result.UnityVersion = options.ForcedUnityVersion!.Split('.').Select(int.Parse).ToArray();
                result.Valid = true;
            }

            ConsoleLogger.ShowVerbose = options.Verbose;
            result.AssemblyToRunAnalysisFor = options.RunAnalysisForAssembly;
            result.WasmFrameworkJsFile = options.WasmFrameworkFilePath;

            result.OutputRootDirectory = options.OutputRootDir;
            
            if(string.IsNullOrEmpty(options.OutputFormatId)) 
                throw new SoftException("No output format specified, so nothing to do!");
            
            Cpp2IlApi.Init();

            try
            {
                result.OutputFormat = OutputFormatRegistry.GetFormat(options.OutputFormatId);
                Logger.VerboseNewline($"Selected output format: {result.OutputFormat.OutputFormatName}");
            }
            catch (Exception e)
            {
                throw new SoftException(e.Message);
            }

            try
            {
                result.ProcessingLayersToRun = options.ProcessorsToUse.Select(ProcessingLayerRegistry.GetById).ToList();
                if(result.ProcessingLayersToRun.Count > 0)
                    Logger.VerboseNewline($"Selected processing layers: {string.Join(", ", result.ProcessingLayersToRun.Select(l => l.Name))}");
                else
                    Logger.VerboseNewline("No processing layers requested");
            }
            catch (Exception e)
            {
                throw new SoftException(e.Message);
            }

            try
            {
                options.ProcessorConfigOptions.Select(c => c.Split('=')).ToList().ForEach(s => result.ProcessingLayerConfigurationOptions.Add(s[0], s[1]));
            }
            catch (IndexOutOfRangeException)
            {
                throw new SoftException("Processor config options must be in the format 'key=value'");
            }

            return result;
        }

        public static int Main(string[] args)
        {
            Console.WriteLine("===Cpp2IL by Samboy063===");
            Console.WriteLine("A Tool to Reverse Unity's \"il2cpp\" Build Process.\n");

            ConsoleLogger.Initialize();

            Logger.InfoNewline("Running on " + Environment.OSVersion.Platform);

            try
            {
                var runtimeArgs = GetRuntimeOptionsFromCommandLine(args);

                return MainWithArgs(runtimeArgs);
            }
            catch (SoftException e)
            {
                Logger.ErrorNewline($"Execution Failed: {e.Message}");
                return -1;
            }
#if !DEBUG
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
#endif
        }

        public static int MainWithArgs(Cpp2IlRuntimeArgs runtimeArgs)
        {
            if (!runtimeArgs.Valid)
                throw new SoftException("Arguments have Valid = false");

            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            if (runtimeArgs.WasmFrameworkJsFile != null)
                try
                {
                    var frameworkJs = File.ReadAllText(runtimeArgs.WasmFrameworkJsFile);
                    var remaps = WasmUtils.ExtractAndParseDynCallRemaps(frameworkJs);
                    Logger.InfoNewline($"Parsed {remaps.Count} dynCall remaps from {runtimeArgs.WasmFrameworkJsFile}");
                    WasmFile.RemappedDynCallFunctions = remaps;
                }
                catch (Exception e)
                {
                    WasmFile.RemappedDynCallFunctions = null;
                    Logger.WarnNewline($"Failed to parse dynCall remaps from Wasm Framework Javascript File: {e}. They will not be used, so you probably won't get method bodies!");
                }
            else
                WasmFile.RemappedDynCallFunctions = null;

            Cpp2IlApi.InitializeLibCpp2Il(runtimeArgs.PathToAssembly, runtimeArgs.PathToMetadata, runtimeArgs.UnityVersion);

            foreach (var processingLayer in runtimeArgs.ProcessingLayersToRun)
            {
                var processorStart = DateTime.Now;
                
                Logger.InfoNewline($"Running processor {processingLayer.Name}...");
                processingLayer.Process(Cpp2IlApi.CurrentAppContext!);
                
                Logger.InfoNewline($"Processor {processingLayer.Name} finished in {(DateTime.Now - processorStart).TotalMilliseconds}ms");
            }

            var outputStart = DateTime.Now;
            
            Logger.InfoNewline($"Outputting as {runtimeArgs.OutputFormat.OutputFormatName} to {runtimeArgs.OutputRootDirectory}...");
            runtimeArgs.OutputFormat.DoOutput(Cpp2IlApi.CurrentAppContext!, runtimeArgs.OutputRootDirectory);
            Logger.InfoNewline($"Finished outputting in {(DateTime.Now - outputStart).TotalMilliseconds}ms");
            
            // if (runtimeArgs.EnableMetadataGeneration)
                // Cpp2IlApi.GenerateMetadataForAllAssemblies(runtimeArgs.OutputRootDirectory);

            // if (runtimeArgs.EnableAnalysis)
                // Cpp2IlApi.PopulateConcreteImplementations();

            CleanupExtractedFiles();

            Logger.InfoNewline("Done.");
            return 0;
        }

        private static void CleanupExtractedFiles()
        {
            foreach (var p in PathsToDeleteOnExit)
            {
                try
                {
                    Logger.InfoNewline($"Cleaning up {p}...");
                    File.Delete(p);
                }
                catch (Exception)
                {
                    //Ignore
                }
            }
        }
    }
}