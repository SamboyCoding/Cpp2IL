using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using CommandLine;
using Cpp2IL.Core;
using Cpp2IL.Core.Exceptions;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Utils;
#if !DEBUG
using Cpp2IL.Core.Exceptions;
#endif
using LibCpp2IL;
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
            {
                //Windows game.
                args.PathToAssembly = Path.Combine(gamePath, "GameAssembly.dll");
                var exeName = Path.GetFileNameWithoutExtension(Directory.GetFiles(gamePath)
                    .FirstOrDefault(f => f.EndsWith(".exe") && !BlacklistedExecutableFilenames.Any(bl => f.EndsWith(bl))));

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

                var gameDataPath = Path.Combine(gamePath, $"{exeName}_Data");
                var uv = Cpp2IlApi.DetermineUnityVersion(unityPlayerPath, gameDataPath);

                if (uv == null)
                {
                    Logger.Warn("Could not determine unity version, probably due to not running on windows and not having any assets files to determine it from. Enter unity version, if known, in the format of (xxxx.x.x), else nothing to fail: ");
                    var userInputUv = Console.ReadLine();
                    uv = userInputUv?.Split('.').Select(int.Parse).ToArray();
                    
                    if(uv == null)
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
            else
            {
                throw new SoftException($"Could not find a valid unity game at {gamePath}");
            }
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

            Cpp2IlApi.InitializeLibCpp2Il(runtimeArgs.PathToAssembly, runtimeArgs.PathToMetadata, runtimeArgs.UnityVersion, runtimeArgs.EnableRegistrationPrompts);

            var missingSwitchSupport = 0;
            var badConditions = 0;
            var allMethodsWithBodies = LibCpp2IlMain.TheMetadata!.methodDefs.Where(m => m.MethodPointer > 0).ToList();
            Logger.InfoNewline($"About to build graph for {allMethodsWithBodies.Count} methods");
            var processed = 0;
            foreach (var m in allMethodsWithBodies)
            {
                var body = X86Utils.GetMethodBodyAtVirtAddressNew(m.MethodPointer, false).ToList();
                X86Utils.TrimInt3s(body);
                var graph = new X86ControlFlowGraph(body);
                try
                {
                    graph.Run();
                    processed++;

                    if (processed % 10 == 0)
                        Logger.InfoNewline($"Processed {processed}");
                }
                catch (NotImplementedException e) when (e.Message.StartsWith("Indirect branch"))
                {
                    missingSwitchSupport++;
                }
                catch (NodeConditionCalculationException)
                {
                    badConditions++;
                }
                catch (Exception e)
                {
                    Logger.InfoNewline($"Failed to generate graph for method {m.HumanReadableSignature} in {m.DeclaringType} at 0x{m.MethodPointer:X}");
                    Logger.InfoNewline($"The error was: {e}");
                    Logger.InfoNewline("The ASM Dump is:\n" + string.Join("\n", body.Select(i => "\t" + i.IP.ToString("x8") + " " + i)));
                    Logger.InfoNewline("The graph dump is:");
                    graph.Print();
                    Logger.ErrorNewline("Failed to generate graph, dumped.");
                    return 1;
                }
            }
            
            Logger.WarnNewline($"Failed to build graph for {missingSwitchSupport} methods due to a lack of switch support");

            Cpp2IlApi.MakeDummyDLLs(runtimeArgs.SuppressAttributes);

#if NET6_0
            //Fix capstone native library loading on non-windows

            try
            {
                var allInstructionsField = typeof(Arm64KeyFunctionAddresses).GetField("_allInstructions", BindingFlags.Instance | BindingFlags.NonPublic);
                var arm64InstructionType = allInstructionsField!.FieldType.GenericTypeArguments.First();
                NativeLibrary.SetDllImportResolver(arm64InstructionType.Assembly, DllImportResolver);
            }
            catch (Exception e)
            {
                Logger.WarnNewline("Unable to hook native library resolving for Capstone. If you're not on windows and analysing an ARM or ARM64 binary, expect this to crash!");
            }
#endif

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

            Cpp2IlApi.RunAttributeRestorationForAllAssemblies(keyFunctionAddresses, parallel: LibCpp2IlMain.MetadataVersion >= 29 || LibCpp2IlMain.Binary!.InstructionSet is InstructionSet.X86_32 or InstructionSet.X86_64);

            Logger.InfoNewline($"Finished Applying Attributes in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

            if (runtimeArgs.EnableAnalysis)
                Cpp2IlApi.PopulateConcreteImplementations();
            
            // Cpp2IlApi.HarmonyPatchCecilForBetterExceptions();

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
            {
                Cpp2IlApi.HarmonyPatchCecilForBetterExceptions();
                Cpp2IlApi.SaveAssemblies(rootDir, new List<AssemblyDefinition> {targetAssembly});
            }
        }


        private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == "capstone")
            {
                // On linux, try .so.4
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
#if NET6_0
                    return NativeLibrary.Load("lib" + libraryName + ".so.4", assembly, searchPath);
#endif
                }
            }

            // Otherwise, fallback to default import resolver.
            return IntPtr.Zero;
        }
    }
}