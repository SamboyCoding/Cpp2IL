using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using CommandLine;
using Cpp2IL.Core;
using Cpp2IL.Core.Utils;
#if !DEBUG
using Cpp2IL.Core.Exceptions;
#endif
using LibCpp2IL;
using LibCpp2IL.Wasm;
using Mono.Cecil;

namespace Cpp2IL
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    internal class Program
    {
        private static readonly List<string> _pathsToDeleteOnExit = new List<string>();

        private static readonly string[] BlacklistedExecutableFilenames =
        {
            "UnityCrashHandler.exe",
            "UnityCrashHandler32.exe",
            "UnityCrashHandler64.exe",
            "install.exe",
            "launch.exe",
            "MelonLoader.Installer.exe"
        };

        private static void ResolvePathsFromCommandLine(string gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
        {
            if (Directory.Exists(gamePath))
                if (Path.GetExtension(gamePath).ToLowerInvariant() == ".app")
                    HandleMachOGamePath(gamePath, ref args);
                else
                    HandleWindowsGamePath(gamePath, inputExeName, ref args);
            else if (File.Exists(gamePath) && Path.GetExtension(gamePath).ToLowerInvariant() == ".apk")
                HandleSingleApk(gamePath, ref args);
            else if (File.Exists(gamePath) && Path.GetExtension(gamePath).ToLowerInvariant() == ".xapk")
                HandleXapk(gamePath, ref args);
            else
                throw new SoftException($"Could not find a valid unity game at {gamePath}");
        }

        private static void HandleWindowsGamePath(string gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
        {
            //Windows game.
            args.PathToAssembly = Path.Combine(gamePath, "GameAssembly.dll");
            var exeName = Path.GetFileNameWithoutExtension(Directory.GetFiles(gamePath)
                .FirstOrDefault(f => f.EndsWith(".exe") && !BlacklistedExecutableFilenames.Any(f.EndsWith)));

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

        private static void HandleSingleApk(string gamePath, ref Cpp2IlRuntimeArgs args)
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

            _pathsToDeleteOnExit.Add(tempFileBinary);
            _pathsToDeleteOnExit.Add(tempFileMeta);

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

        private static void HandleXapk(string gamePath, ref Cpp2IlRuntimeArgs args)
        {
            //XAPK file
            //Contains two APKs - one starting with `config.` and one with the package name
            //The config one is architecture-specific and so contains the binary
            //The other contains the metadata
            
            Logger.InfoNewline($"Attempting to extract required files from XAPK {gamePath}", "XAPK");
            
            using var xapkStream = File.OpenRead(gamePath);
            using var xapkZip = new ZipArchive(xapkStream);
            
            var configApk = xapkZip.Entries.FirstOrDefault(e => e.FullName.Contains("config.") && e.FullName.EndsWith(".apk"));
            var mainApk = xapkZip.Entries.FirstOrDefault(e => e != configApk && e.FullName.EndsWith(".apk"));
            
            Logger.InfoNewline($"Identified APKs inside XAPK - config: {configApk?.FullName}, mainPackage: {mainApk?.FullName}", "XAPK");
            
            if (configApk == null)
                throw new SoftException("Could not find a config apk inside the XAPK");
            if (mainApk == null)
                throw new SoftException("Could not find a main apk inside the XAPK");
            
            using var configZip = new ZipArchive(configApk.Open());
            using var mainZip = new ZipArchive(mainApk.Open());
            var binary = configZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("libil2cpp.so"));
            var globalMetadata = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("global-metadata.dat"));
            
            var globalgamemanagers = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("globalgamemanagers"));
            var dataUnity3d = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("data.unity3d"));
            
            if (binary == null)
                throw new SoftException("Could not find libil2cpp.so inside the config APK");
            if(globalMetadata == null)
                throw new SoftException("Could not find global-metadata.dat inside the main APK");
            if(globalgamemanagers == null && dataUnity3d == null)
                throw new SoftException("Could not find globalgamemanagers or data.unity3d inside the main APK");
            
            var tempFileBinary = Path.GetTempFileName();
            var tempFileMeta = Path.GetTempFileName();

            _pathsToDeleteOnExit.Add(tempFileBinary);
            _pathsToDeleteOnExit.Add(tempFileMeta);

            Logger.InfoNewline($"Extracting XAPK/{configApk.Name}/{binary.FullName} to {tempFileBinary}", "XAPK");
            binary.ExtractToFile(tempFileBinary, true);
            Logger.InfoNewline($"Extracting XAPK{mainApk.Name}/{globalMetadata.FullName} to {tempFileMeta}", "XAPK");
            globalMetadata.ExtractToFile(tempFileMeta, true);

            args.PathToAssembly = tempFileBinary;
            args.PathToMetadata = tempFileMeta;

            if (globalgamemanagers != null)
            {
                Logger.InfoNewline("Reading globalgamemanagers to determine unity version...", "XAPK");
                var ggmBytes = new byte[0x40];
                using var ggmStream = globalgamemanagers.Open();
                ggmStream.Read(ggmBytes, 0, 0x40);

                args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(ggmBytes);
            }
            else
            {
                Logger.InfoNewline("Reading data.unity3d to determine unity version...", "XAPK");
                using var du3dStream = dataUnity3d!.Open();

                args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(du3dStream);
            }

            Logger.InfoNewline($"Determined game's unity version to be {string.Join(".", args.UnityVersion)}", "XAPK");

            args.Valid = true;
        }

        private static void HandleMachOGamePath(string gamePath, ref Cpp2IlRuntimeArgs args)
        {
            //APP
            //Metadata: Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat
            //Binary: Contents/Frameworks/GameAssembly.dylib

            Logger.InfoNewline($"Attempting to extract required files from APP {gamePath}", "APP");

            var binary = Path.Combine(gamePath, "Contents", "Frameworks", "GameAssembly.dylib");
            var globalMetadata = Path.Combine(gamePath, "Contents", "Resources", "Data", "il2cpp_data", "Metadata", "global-metadata.dat");
            var globalgamemanagers = Path.Combine(gamePath, "Contents", "Resources", "Data", "globalgamemanagers");

            if (binary == null)
                throw new SoftException("Could not find GameAssembly.dylib inside the app");
            if (globalMetadata == null)
                throw new SoftException("Could not find global-metadata.dat inside the app");
            if (globalgamemanagers == null)
                throw new SoftException("Could not find globalgamemanagers inside the app");

            args.PathToAssembly = binary;
            args.PathToMetadata = globalMetadata;

            Logger.VerboseNewline("Attempting to get unity version...");

            Logger.InfoNewline("Reading globalgamemanagers to determine unity version...", "APP");
            var uv = (File.Exists(globalgamemanagers) ? 
                        Cpp2IlApi.GetVersionFromGlobalGameManagers(File.ReadAllBytes(globalgamemanagers)) : null);
            Logger.VerboseNewline($"First-attempt unity version detection gave: {(uv == null ? "null" : string.Join(".", uv))}");

            if (uv == null)
            {
                Logger.Warn("Could not determine unity version, probably due to not running on windows and not having any assets files to determine it from. Enter unity version, if known, in the format of (xxxx.x.x), else nothing to fail: ");
                var userInputUv = Console.ReadLine();
                uv = userInputUv?.Split('.').Select(int.Parse).ToArray();

                if (uv == null)
                    throw new SoftException("Failed to determine unity version. If you're not running on windows, I need a globalgamemanagers file, or you need to use the force options.");
            }

            args.UnityVersion = uv;

            Logger.InfoNewline($"Determined game's unity version to be {string.Join(".", args.UnityVersion)}", "APP");

            args.Valid = true;
        }

        private static Cpp2IlRuntimeArgs GetRuntimeOptionsFromCommandLine(string[] commandLine)
        {
            var parserResult = Parser.Default.ParseArguments<CommandLineArgs>(commandLine);

            if (parserResult is NotParsed<CommandLineArgs> notParsed && notParsed.Errors.Count() == 1 && notParsed.Errors.All(e => e.Tag == ErrorType.VersionRequestedError || e.Tag == ErrorType.HelpRequestedError))
                //Version or help requested
                Environment.Exit(0);

            if (!(parserResult is Parsed<CommandLineArgs> {Value: { } options}))
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

            result.EnableAnalysis = !options.SkipAnalysis;
            result.EnableMetadataGeneration = !options.SkipMetadataTextFiles;
            result.EnableRegistrationPrompts = !options.DisableRegistrationPrompts;
            result.EnableVerboseLogging = options.Verbose;
            result.EnableIlToAsm = options.EnableIlToAsm;
            result.SuppressAttributes = options.SuppressAttributes;
            result.Parallel = options.Parallel;
            result.AssemblyToRunAnalysisFor = options.RunAnalysisForAssembly;
            result.AnalyzeAllAssemblies = options.AnalyzeAllAssemblies;
            result.IlToAsmContinueThroughErrors = options.ThrowSafetyOutTheWindow;
            result.DisableMethodDumps = options.DisableMethodDumps;
            result.SimpleAttributeRestoration = options.SimpleAttributeRestoration;
            result.WasmFrameworkJsFile = options.WasmFrameworkFilePath;

            if (options.UserIsImpatient)
            {
                result.Parallel = true;
                result.DisableMethodDumps = true;
                result.EnableIlToAsm = true;
                result.IlToAsmContinueThroughErrors = true;
                result.EnableMetadataGeneration = false;
            }
            
            if (result.DisableMethodDumps)
                result.AnalysisLevel = AnalysisLevel.IL_ONLY;
            else
                result.AnalysisLevel = (AnalysisLevel) options.AnalysisLevel;

            if (result.EnableIlToAsm)
            {
                Logger.WarnNewline("!!!!!!!!!!You have enabled IL-To-ASM. If this breaks, it breaks.!!!!!!!!!!");
            }

            if (result.IlToAsmContinueThroughErrors)
            {
                Logger.ErrorNewline("!!!!!!!!!!Throwing safety out the window, as you requested! Forget \"If this breaks, it breaks\", this probably WILL break!!!!!!!!!!");
            }

            result.OutputRootDirectory = options.OutputRootDir;

            return result;
        }

        public static int Main(string[] args)
        {
            Console.WriteLine("===Cpp2IL by Samboy063===");
            Console.WriteLine("A Tool to Reverse Unity's \"il2cpp\" Build Process.\n");

            ConsoleLogger.Initialize();

            Logger.InfoNewline("Running on " + Environment.OSVersion.Platform);

#if !DEBUG
            try
            {
#endif
            var runtimeArgs = GetRuntimeOptionsFromCommandLine(args);

            return MainWithArgs(runtimeArgs);
#if !DEBUG
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
#endif
        }

        public static int MainWithArgs(Cpp2IlRuntimeArgs runtimeArgs)
        {
            if (!runtimeArgs.Valid)
                throw new SoftException("Arguments have Valid = false");

            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            ConsoleLogger.ShowVerbose = runtimeArgs.EnableVerboseLogging;

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

            Cpp2IlApi.InitializeLibCpp2Il(runtimeArgs.PathToAssembly, runtimeArgs.PathToMetadata, runtimeArgs.UnityVersion, runtimeArgs.EnableRegistrationPrompts);

            Cpp2IlApi.MakeDummyDLLs(runtimeArgs.SuppressAttributes);

            if (runtimeArgs.EnableMetadataGeneration)
                Cpp2IlApi.GenerateMetadataForAllAssemblies(runtimeArgs.OutputRootDirectory);

            BaseKeyFunctionAddresses? keyFunctionAddresses = null;

            //We need to run key function sweep if we can for attribute restoration
            //or if we want to analyze. But we DON'T need it for restoration on v29
            var attributeRestorationNeedsKfas = LibCpp2IlMain.MetadataVersion < 29 && !runtimeArgs.SimpleAttributeRestoration;
            var canGetKfas = LibCpp2IlMain.Binary?.InstructionSet is not InstructionSet.ARM32;
            if (canGetKfas && (attributeRestorationNeedsKfas || runtimeArgs.EnableAnalysis))
            {
                Logger.InfoNewline("Running Scan for Known Functions...");

                //This part involves decompiling known functions to search for other function calls
                keyFunctionAddresses = Cpp2IlApi.ScanForKeyFunctionAddresses();
            }

            Logger.InfoNewline($"Applying type, method, and field attributes for {Cpp2IlApi.GeneratedAssemblies.Count} assemblies...This may take a couple of seconds");
            var start = DateTime.Now;

            Cpp2IlApi.RunAttributeRestorationForAllAssemblies(runtimeArgs.SimpleAttributeRestoration ? null : keyFunctionAddresses, parallel: LibCpp2IlMain.MetadataVersion >= 29 || LibCpp2IlMain.Binary!.InstructionSet is InstructionSet.X86_32 or InstructionSet.X86_64);

            Logger.InfoNewline($"Finished Applying Attributes in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

            if (runtimeArgs.EnableAnalysis)
                Cpp2IlApi.PopulateConcreteImplementations();

            Cpp2IlApi.HarmonyPatchCecilForBetterExceptions();

            Cpp2IlApi.SaveAssemblies(runtimeArgs.OutputRootDirectory);

            if (runtimeArgs.EnableAnalysis)
            {
                if (runtimeArgs.AnalyzeAllAssemblies)
                {
                    foreach (var assemblyDefinition in Cpp2IlApi.GeneratedAssemblies)
                    {
                        DoAnalysisForAssembly(assemblyDefinition.Name.Name, runtimeArgs.AnalysisLevel, runtimeArgs.OutputRootDirectory, keyFunctionAddresses!, runtimeArgs.EnableIlToAsm, runtimeArgs.Parallel, runtimeArgs.IlToAsmContinueThroughErrors, runtimeArgs.DisableMethodDumps);
                    }
                }
                else
                {
                    DoAnalysisForAssembly(runtimeArgs.AssemblyToRunAnalysisFor, runtimeArgs.AnalysisLevel, runtimeArgs.OutputRootDirectory, keyFunctionAddresses!, runtimeArgs.EnableIlToAsm, runtimeArgs.Parallel, runtimeArgs.IlToAsmContinueThroughErrors, runtimeArgs.DisableMethodDumps);
                }
            }

            foreach (var p in _pathsToDeleteOnExit)
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

            Logger.InfoNewline("Done.");
            return 0;
        }

        private static void DoAnalysisForAssembly(string assemblyName, AnalysisLevel analysisLevel, string rootDir, BaseKeyFunctionAddresses keyFunctionAddresses, bool doIlToAsm, bool parallel, bool continueThroughErrors, bool skipDumps)
        {
            var targetAssembly = Cpp2IlApi.GetAssemblyByName(assemblyName);

            if (targetAssembly == null)
                return;

            Logger.InfoNewline($"Running Analysis for {assemblyName}.dll...");

            Cpp2IlApi.AnalyseAssembly(analysisLevel, targetAssembly, keyFunctionAddresses, skipDumps ? null : Path.Combine(rootDir, "types"), parallel, continueThroughErrors);

            if (doIlToAsm)
                Cpp2IlApi.SaveAssemblies(rootDir, new List<AssemblyDefinition> {targetAssembly});
        }
    }
}
