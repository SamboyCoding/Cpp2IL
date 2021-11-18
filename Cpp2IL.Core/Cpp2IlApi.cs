using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Analysis.Actions.x86.Important;
using Cpp2IL.Core.Exceptions;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using LibCpp2IL.Logging;
using Mono.Cecil;

namespace Cpp2IL.Core
{
    public static class Cpp2IlApi
    {
        public static List<AssemblyDefinition> GeneratedAssemblies => SharedState.AssemblyList.ToList(); //Shallow copy
        internal static bool IlContinueThroughErrors;

        public static AssemblyDefinition? GetAssemblyByName(string name) =>
            SharedState.AssemblyList.Find(a => a.Name.Name == name);

        public static int[] DetermineUnityVersion(string unityPlayerPath, string gameDataPath)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && !string.IsNullOrEmpty(unityPlayerPath))
            {
                var unityVer = FileVersionInfo.GetVersionInfo(unityPlayerPath);

                return new[] {unityVer.FileMajorPart, unityVer.FileMinorPart, unityVer.FileBuildPart};
            }

            if(!string.IsNullOrEmpty(gameDataPath))
            {
                //Globalgamemanagers
                var globalgamemanagersPath = Path.Combine(gameDataPath, "globalgamemanagers");
                if(File.Exists(globalgamemanagersPath))
                {
                    var ggmBytes = File.ReadAllBytes(globalgamemanagersPath);
                    return GetVersionFromGlobalGameManagers(ggmBytes);
                }
                
                //Data.unity3d
                var dataPath = Path.Combine(gameDataPath, "data.unity3d");
                if(File.Exists(dataPath))
                {
                    using var dataStream = File.OpenRead(dataPath);
                    return GetVersionFromDataUnity3D(dataStream);
                }
            }

            return null;
        }

        public static int[] GetVersionFromGlobalGameManagers(byte[] ggmBytes)
        {
            var verString = new StringBuilder();
            var idx = 0x14;
            while (ggmBytes[idx] != 0)
            {
                verString.Append(Convert.ToChar(ggmBytes[idx]));
                idx++;
            }

            var unityVer = verString.ToString();

            if (!unityVer.Contains("f"))
            {
                idx = 0x30;
                verString = new StringBuilder();
                while (ggmBytes[idx] != 0)
                {
                    verString.Append(Convert.ToChar(ggmBytes[idx]));
                    idx++;
                }

                unityVer = verString.ToString();
            }

            unityVer = unityVer[..unityVer.IndexOf("f", StringComparison.Ordinal)];
            return unityVer.Split('.').Select(int.Parse).ToArray();
        }

        public static int[] GetVersionFromDataUnity3D(Stream fileStream)
        {
            //data.unity3d is a bundle file and it's used on later unity versions.
            //These files are usually really large and we only want the first couple bytes, so it's done via a stream.
            //e.g.: Secret Neighbour
            //Fake unity version at 0xC, real one at 0x12
            
            var verString = new StringBuilder();

            if (fileStream.CanSeek)
                fileStream.Seek(0x12, SeekOrigin.Begin);
            else
                fileStream.Read(new byte[0x12], 0, 0x12);
            
            while (true)
            {
                var read = fileStream.ReadByte();
                if (read == 0)
                {
                    //I'm using a while true..break for this, shoot me.
                    break;
                }
                verString.Append(Convert.ToChar(read));
            }

            var unityVer = verString.ToString();
            
            unityVer = unityVer[..unityVer.IndexOf("f", StringComparison.Ordinal)];
            return unityVer.Split('.').Select(int.Parse).ToArray();
        }

        private static void ConfigureLib(bool allowUserToInputAddresses)
        {
            //Set this flag from the options
            LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput = allowUserToInputAddresses;

            //We have to have this on, despite the cost, because we need them for attribute restoration
            LibCpp2IlMain.Settings.DisableMethodPointerMapping = false;

            LibLogger.Writer = new LibLogWriter();
        }

        [Obsolete("Use InitializeLibCpp2Il(string, string, int[], bool) instead as verbose is deprecated", true)]
        public static void InitializeLibCpp2Il(string assemblyPath, string metadataPath, int[] unityVersion, bool verbose = false, bool allowUserToInputAddresses = false)
            => InitializeLibCpp2Il(assemblyPath, metadataPath, unityVersion, allowUserToInputAddresses);

        public static void InitializeLibCpp2Il(string assemblyPath, string metadataPath, int[] unityVersion, bool allowUserToInputAddresses = false)
        {
            if (IsLibInitialized())
                ResetInternalState();
            
            ConfigureLib(allowUserToInputAddresses);
            FixCapstoneLib();

            try
            {
                if (!LibCpp2IlMain.LoadFromFile(assemblyPath, metadataPath, unityVersion))
                    throw new Exception("Initialization with LibCpp2Il failed");
                
                LibCpp2IlMain.Binary!.AllCustomAttributeGenerators.ToList().ForEach(ptr => SharedState.AttributeGeneratorStarts.Add(ptr));
            }
            catch (Exception e)
            {
                throw new LibCpp2ILInitializationException("Fatal Exception initializing LibCpp2IL!", e);
            }
        }

        [Obsolete("Use InitializeLibCpp2Il(byte[], string, int[], bool) instead as verbose is deprecated", true)]
        public static void InitializeLibCpp2Il(byte[] assemblyData, byte[] metadataData, int[] unityVersion, bool verbose = false, bool allowUserToInputAddresses = false)
            => InitializeLibCpp2Il(assemblyData, metadataData, unityVersion, allowUserToInputAddresses);

        public static void InitializeLibCpp2Il(byte[] assemblyData, byte[] metadataData, int[] unityVersion, bool allowUserToInputAddresses = false)
        {
            if (IsLibInitialized())
                ResetInternalState();
            
            ConfigureLib(allowUserToInputAddresses);
            FixCapstoneLib();

            try
            {
                if (!LibCpp2IlMain.Initialize(assemblyData, metadataData, unityVersion))
                    throw new Exception("Initialization with LibCpp2Il failed");

                LibCpp2IlMain.Binary!.AllCustomAttributeGenerators.ToList().ForEach(ptr => SharedState.AttributeGeneratorStarts.Add(ptr));
            }
            catch (Exception e)
            {
                throw new LibCpp2ILInitializationException("Fatal Exception initializing LibCpp2IL!", e);
            }
        }

        private static void ResetInternalState()
        {
            SharedState.Clear();

            MiscUtils.Reset();

            AttributeRestorer.Reset();

            AssemblyPopulator.Reset();

            CallExceptionThrowerFunction.Reset();

            LibCpp2IlMain.Reset();
        }

        public static List<AssemblyDefinition> MakeDummyDLLs(bool suppressAttributes = false)
        {
            CheckLibInitialized();

            Logger.InfoNewline("Building assemblies...This may take some time.");
            var start = DateTime.Now;

            var resolver = new RegistryAssemblyResolver();
            var moduleParams = new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                AssemblyResolver = resolver,
                MetadataResolver = new MetadataResolver(resolver)
            };

            //Make stub types
            var startTwo = DateTime.Now;
            Logger.Verbose("\tPre-generating stubs...");
            var Assemblies = StubAssemblyBuilder.BuildStubAssemblies(LibCpp2IlMain.TheMetadata!, moduleParams);
            Assemblies.ForEach(resolver.Register);
            Logger.VerboseNewline($"OK ({(DateTime.Now - startTwo).TotalMilliseconds}ms)");

            //Configure utils class
            MiscUtils.BuildPrimitiveMappings();

            //Set base types and interfaces
            startTwo = DateTime.Now;
            Logger.Verbose("\tConfiguring Hierarchy...");
            AssemblyPopulator.ConfigureHierarchy();
            Logger.VerboseNewline($"OK ({(DateTime.Now - startTwo).TotalMilliseconds}ms)");

            foreach (var imageDef in LibCpp2IlMain.TheMetadata!.imageDefinitions)
            {
                var startAssem = DateTime.Now;

                Logger.Verbose($"\tPopulating {imageDef.Name}...");

                AssemblyPopulator.PopulateStubTypesInAssembly(imageDef, suppressAttributes);

                Logger.VerboseNewline($"Done ({(DateTime.Now - startAssem).TotalMilliseconds}ms)");
            }

            Logger.InfoNewline($"Finished Building Assemblies in {(DateTime.Now - start).TotalMilliseconds:F0}ms");
            Logger.InfoNewline("Fixing up explicit overrides. Any warnings you see here aren't errors - they usually indicate improperly stripped or obfuscated types, but this is not a big deal. This should only take a second...");
            start = DateTime.Now;

            //Fixup explicit overrides.
            foreach (var imageDef in LibCpp2IlMain.TheMetadata.imageDefinitions)
                AssemblyPopulator.FixupExplicitOverridesInAssembly(imageDef);

            Logger.InfoNewline($"Fixup complete ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");

            SharedState.AssemblyList.AddRange(Assemblies);

            return Assemblies;
        }

        public static BaseKeyFunctionAddresses ScanForKeyFunctionAddresses()
        {
            CheckLibInitialized();

            BaseKeyFunctionAddresses keyFunctionAddresses = LibCpp2IlMain.Binary!.InstructionSet switch
            {
                InstructionSet.X86_32 => new X86KeyFunctionAddresses(),
                InstructionSet.X86_64 => new X86KeyFunctionAddresses(),
                InstructionSet.ARM64 => new Arm64KeyFunctionAddresses(),
                InstructionSet.ARM32 => throw new UnsupportedInstructionSetException(), //todo
                _ => throw new ArgumentOutOfRangeException()
            };

            keyFunctionAddresses.Find();
            return keyFunctionAddresses;
        }

        [SuppressMessage("ReSharper", "ReturnValueOfPureMethodIsNotUsed")]
        public static void RunAttributeRestorationForAllAssemblies(BaseKeyFunctionAddresses? keyFunctionAddresses = null, bool parallel = true)
        {
            CheckLibInitialized();

            if (keyFunctionAddresses?.il2cpp_object_new is 0 && keyFunctionAddresses.il2cpp_type_get_object is 0 && keyFunctionAddresses.il2cpp_string_new is 0)
            {
                Logger.WarnNewline("Key function addresses are garbage - binary probably has no export table. They will not be used.", "Attribute Restoration");
                keyFunctionAddresses = null;
            }

            var enumerable = (IEnumerable<AssemblyDefinition>) SharedState.AssemblyList;

            if (parallel)
                enumerable = enumerable.AsParallel();

            enumerable.Select(def =>
            {
                if(!parallel && LibCpp2IlMain.MetadataVersion < 29)
                    Logger.Verbose($"Processing {def.Name.Name}...");
                RunAttributeRestorationForAssembly(def, keyFunctionAddresses);
                
                if(LibCpp2IlMain.MetadataVersion < 29)
                    Logger.VerboseNewline($"Finished processing {def.Name.Name}");
                return true;
            }).ToList(); //Force full evaluation
        }

        public static void RunAttributeRestorationForAssembly(AssemblyDefinition assembly, BaseKeyFunctionAddresses? keyFunctionAddresses = null)
        {
            CheckLibInitialized();

            if (LibCpp2IlMain.MetadataVersion >= 29)
            {
                //V29: Attributes are stored in metadata. This process becomes a lot simpler.
                AttributeRestorerPost29.ApplyCustomAttributesToAllTypesInAssembly(assembly);
                return;
            }

            switch (LibCpp2IlMain.Binary!.InstructionSet)
            {
                case InstructionSet.X86_32:
                case InstructionSet.X86_64:
                    AttributeRestorer.ApplyCustomAttributesToAllTypesInAssembly<Iced.Intel.Instruction>(assembly, keyFunctionAddresses);
                    break;
                case InstructionSet.ARM32:
                    AttributeRestorer.ApplyCustomAttributesToAllTypesInAssembly<ArmInstruction>(assembly, keyFunctionAddresses);
                    break;
                case InstructionSet.ARM64:
                    AttributeRestorer.ApplyCustomAttributesToAllTypesInAssembly<Arm64Instruction>(assembly, keyFunctionAddresses);
                    break;
                default:
                    throw new UnsupportedInstructionSetException();
            }
        }

        public static void GenerateMetadataForAllAssemblies(string rootFolder)
        {
            CheckLibInitialized();

            foreach (var assemblyDefinition in SharedState.AssemblyList)
                GenerateMetadataForAssembly(rootFolder, assemblyDefinition);
        }

        public static void GenerateMetadataForAssembly(string rootFolder, AssemblyDefinition assemblyDefinition)
        {
            foreach (var mainModuleType in assemblyDefinition.MainModule.Types.Where(mainModuleType => mainModuleType.Namespace != AssemblyPopulator.InjectedNamespaceName))
            {
                GenerateMetadataForType(rootFolder, mainModuleType);
            }
        }

        public static void GenerateMetadataForType(string rootFolder, TypeDefinition typeDefinition)
        {
            CheckLibInitialized();

            var assemblyPath = Path.Combine(rootFolder, "types", typeDefinition.Module.Assembly.Name.Name);
            if (!Directory.Exists(assemblyPath))
                Directory.CreateDirectory(assemblyPath);

            File.WriteAllText(
                Path.Combine(assemblyPath, typeDefinition.Name.Replace("<", "_").Replace(">", "_").Replace("|", "_") + "_metadata.txt"),
                AssemblyPopulator.BuildWholeMetadataString(typeDefinition)
            );
        }

        public static void SaveAssemblies(string toWhere) => SaveAssemblies(toWhere, GeneratedAssemblies);

        public static void SaveAssemblies(string toWhere, List<AssemblyDefinition> assemblies)
        {
            Logger.InfoNewline($"Saving {assemblies.Count} assembl{(assemblies.Count != 1 ? "ies" : "y")} to " + toWhere + "...");

            if (!Directory.Exists(toWhere))
            {
                Logger.VerboseNewline($"\tSave directory does not exist. Creating...");
                Directory.CreateDirectory(toWhere);
            }

            foreach (var assembly in assemblies)
            {
                var dllPath = Path.Combine(toWhere, assembly.MainModule.Name);

                //Remove NetCore Dependencies 
                var reference = assembly.MainModule.AssemblyReferences.FirstOrDefault(a => a.Name == "System.Private.CoreLib");
                if (reference != null)
                    assembly.MainModule.AssemblyReferences.Remove(reference);

                try
                {
                    assembly.Write(dllPath);
                }
                catch (Exception e)
                {
                    throw new DllSaveException(dllPath, e);
                }
            }
        }

        [Obsolete("Use (AnalysisLevel, AssemblyDefinition, BaseKeyFunctionAddresses, string, bool, *bool*)")]
        public static void AnalyseAssembly(AnalysisLevel analysisLevel, AssemblyDefinition assembly, BaseKeyFunctionAddresses keyFunctionAddresses, string methodOutputDir, bool parallel) =>
            AnalyseAssembly(analysisLevel, assembly, keyFunctionAddresses, methodOutputDir, parallel,  false);

        /// <summary>
        /// Analyze the given assembly, populating method bodies within it with IL if restoration succeeds or continueThroughErrors is set.
        /// </summary>
        /// <param name="analysisLevel">The level of analysis to save *to the method dump file*. Has no effect if no output directory is provided</param>
        /// <param name="assembly">The assembly to analyze</param>
        /// <param name="keyFunctionAddresses">A BaseKeyFunctionAddresses object, populated with the addresses of il2cpp exports, for analysis purposes.</param>
        /// <param name="methodOutputDir">The directory to create method dumps in. If null, they won't be created.</param>
        /// <param name="parallel">True to execute analysis in parallel (using all cpu cores), false to run on a single core (much slower)</param>
        /// <param name="continueThroughErrors">True to try and bruteforce getting as much IL saved to the assembly as possible, false to bail out if any irregularities are detected.</param>
        /// <exception cref="ArgumentNullException">If assembly or keyFunctionAddresses is null</exception>
        /// <exception cref="UnsupportedInstructionSetException">If the instruction set of the IL2CPP binary is not supported for analysis yet.</exception>
        public static void AnalyseAssembly(AnalysisLevel analysisLevel, AssemblyDefinition assembly, BaseKeyFunctionAddresses keyFunctionAddresses, string? methodOutputDir, bool parallel, bool continueThroughErrors)
        {
            CheckLibInitialized();

            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            if (keyFunctionAddresses == null && LibCpp2IlMain.Binary!.InstructionSet is InstructionSet.X86_32 or InstructionSet.X86_64)
                throw new ArgumentNullException(nameof(keyFunctionAddresses));

            IlContinueThroughErrors = continueThroughErrors;
            
            AsmAnalyzerX86.FAILED_METHODS = 0;
            AsmAnalyzerX86.SUCCESSFUL_METHODS = 0;

            if (methodOutputDir != null)
            {
                Logger.InfoNewline("Dumping method bytes to " + methodOutputDir, "Analyze");
                Directory.CreateDirectory(Path.Combine(methodOutputDir, assembly.Name.Name, "method_dumps"));
            }

            var counter = 0;
            var toProcess = assembly.MainModule.Types.Where(t => t.Namespace != AssemblyPopulator.InjectedNamespaceName).ToList();
            toProcess.AddRange(toProcess.SelectMany(t => t.NestedTypes).ToList());
            //Sort alphabetically by type.
            toProcess.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
            var thresholds = new[] {10, 20, 30, 40, 50, 60, 70, 80, 90, 100}.ToList();
            var nextThreshold = thresholds.First();
            
            Logger.InfoNewline($"This assembly contains {toProcess.Count} types. Assuming an average rate of 20 types per second, this will take approximately {toProcess.Count / 20} seconds, or {toProcess.Count / 20f / 60f:f1} minutes, to process.");

            var numProcessed = 0;

            var startTime = DateTime.Now;

            thresholds.RemoveAt(0);

            void ProcessType(TypeDefinition type)
            {
                counter++;
                var pct = 100 * ((decimal) counter / toProcess.Count);
                if (pct > nextThreshold)
                {
                    lock (thresholds)
                    {
                        //Check again to prevent races
                        if (pct > nextThreshold)
                        {
                            var elapsedSoFar = DateTime.Now - startTime;
                            var rate = counter / elapsedSoFar.TotalSeconds;
                            var remaining = toProcess.Count - counter;
                            Logger.InfoNewline($"{nextThreshold}% ({counter} classes in {Math.Round(elapsedSoFar.TotalSeconds)} sec, ~{Math.Round(rate)} classes / sec, {remaining} classes remaining, approx {Math.Round(remaining / rate + 5)} sec remaining)", "Analyze");
                            nextThreshold = thresholds.First();
                            thresholds.RemoveAt(0);
                        }
                    }
                }

                string? filename = null;
                StringBuilder? typeDump = null;
                
                if (methodOutputDir != null)
                {
                    var fileSafeTypeName = type.Name;

                    if (type.DeclaringType != null)
                        fileSafeTypeName = $"{type.DeclaringType.Name}--NestedType--{fileSafeTypeName}";

                    fileSafeTypeName = fileSafeTypeName
                        .Replace("<", "_")
                        .Replace(">", "_")
                        .Replace("|", "_")
                        .Replace("{", "_")
                        .Replace("}", "_");

                    var methodDumpDir = Path.Combine(methodOutputDir, assembly.Name.Name, "method_dumps");

                    var ns = type.Namespace;
                    if (type.DeclaringType != null)
                        ns = type.DeclaringType.Namespace;

                    if (!string.IsNullOrEmpty(ns))
                    {
                        ns = ns.Replace("<", "_")
                            .Replace(">", "_")
                            .Replace("|", "_")
                            .Replace("{", "_")
                            .Replace("}", "_");
                        
                        methodDumpDir = Path.Combine(new[] { methodDumpDir }.Concat(ns.Split('.')).ToArray());

                        if (!Directory.Exists(methodDumpDir))
                            Directory.CreateDirectory(methodDumpDir);
                    }

                    filename = Path.Combine(methodDumpDir, $"{fileSafeTypeName}_methods.txt");

                    typeDump = new StringBuilder("Type: " + type.Name + "\n\n");
                }

                foreach (var methodDefinition in type.Methods)
                {
                    try
                    {
                        var methodStart = methodDefinition.AsUnmanaged().MethodPointer;

                        if (methodStart == 0)
                            //No body
                            continue;

                        IAsmAnalyzer dumper = LibCpp2IlMain.Binary?.InstructionSet switch
                        {
                            InstructionSet.X86_32 or InstructionSet.X86_64 => new AsmAnalyzerX86(methodDefinition, methodStart, keyFunctionAddresses!),
                            InstructionSet.ARM32 => new AsmAnalyzerArmV7(methodDefinition, methodStart, keyFunctionAddresses!),
                            InstructionSet.ARM64 => new AsmAnalyzerArmV8A(methodDefinition, methodStart, keyFunctionAddresses!),
                            _ => throw new UnsupportedInstructionSetException()
                        };

                        try
                        {
                            dumper.AnalyzeMethod();
                            dumper.RunActionPostProcessors();
                        }
                        catch (AnalysisExceptionRaisedException)
                        {
                            //ignore, already logged
                        }

                        switch (analysisLevel)
                        {
                            case AnalysisLevel.PRINT_ALL:
                                dumper.BuildMethodFunctionality();

                                if (typeDump != null)
                                {
                                    typeDump.Append(dumper.GetFullDumpNoIL());
                                    typeDump.Append(dumper.BuildILToString());
                                }

                                break;
                            case AnalysisLevel.SKIP_ASM:
                                dumper.BuildMethodFunctionality();

                                if (typeDump != null)
                                {
                                    typeDump.Append(dumper.GetWordyFunctionality());
                                    typeDump.Append(dumper.GetPseudocode());
                                    typeDump.Append(dumper.BuildILToString());
                                }

                                break;
                            case AnalysisLevel.SKIP_ASM_AND_SYNOPSIS:
                                if (typeDump != null)
                                {
                                    typeDump.Append(dumper.GetPseudocode());
                                    typeDump.Append(dumper.BuildILToString());
                                }

                                break;
                            case AnalysisLevel.PSUEDOCODE_ONLY:
                                typeDump?.Append(dumper.GetPseudocode());
                                break;
                            case AnalysisLevel.IL_ONLY:
                                typeDump?.Append(dumper.BuildILToString());
                                break;
                            case AnalysisLevel.NONE:
                                break;
                        }

                        Interlocked.Increment(ref numProcessed);
                    }
                    catch (AnalysisExceptionRaisedException)
                    {
                        //Ignore, logged already.
                    }
                    catch (Exception e)
                    {
                        Logger.WarnNewline($"Failed to dump method {methodDefinition.FullName} {e}", "Analyze");
                    }
                }

                if (filename is null || typeDump is null) return;
                
                lock (type) File.WriteAllText(filename, typeDump.ToString());
            }

            if (parallel)
                toProcess.AsParallel().ForAll(ProcessType);
            else
                toProcess.ForEach(ProcessType);

            var elapsed = DateTime.Now - startTime;
            Logger.InfoNewline($"Finished processing {numProcessed} methods in {elapsed.Ticks} ticks (about {Math.Round(elapsed.TotalSeconds, 1)} seconds), at an overall rate of about {Math.Round(toProcess.Count / elapsed.TotalSeconds)} types/sec, {Math.Round(numProcessed / elapsed.TotalSeconds)} methods/sec", "Analyze");

            if (analysisLevel != AnalysisLevel.PSUEDOCODE_ONLY)
            {
                var total = AsmAnalyzerX86.SUCCESSFUL_METHODS + AsmAnalyzerX86.FAILED_METHODS;
                var successPercent = 100;
                if (total != 0)
                    successPercent = AsmAnalyzerX86.SUCCESSFUL_METHODS * 100 / total;

                Logger.InfoNewline($"Overall analysis success rate: {successPercent}% ({AsmAnalyzerX86.SUCCESSFUL_METHODS}) of {total} methods.");
            }
        }

        public static void PopulateConcreteImplementations()
        {
            CheckLibInitialized();

            Logger.InfoNewline("Populating Concrete Implementation Table...");

            foreach (var def in LibCpp2IlMain.TheMetadata!.typeDefs)
            {
                if (def.IsAbstract)
                    continue;

                var baseTypeReflectionData = def.BaseType;
                while (baseTypeReflectionData != null)
                {
                    if (baseTypeReflectionData.baseType == null)
                        break;

                    if (baseTypeReflectionData.isType && baseTypeReflectionData.baseType.IsAbstract && !SharedState.ConcreteImplementations.ContainsKey(baseTypeReflectionData.baseType))
                        SharedState.ConcreteImplementations[baseTypeReflectionData.baseType] = def;

                    baseTypeReflectionData = baseTypeReflectionData.baseType.BaseType;
                }
            }
        }

        public static void HarmonyPatchCecilForBetterExceptions() => Cpp2IlHarmonyPatches.Install();

        private static bool IsLibInitialized()
        {
            return LibCpp2IlMain.Binary != null && LibCpp2IlMain.TheMetadata != null;
        }

        private static void CheckLibInitialized()
        {
            if (!IsLibInitialized())
                throw new LibraryNotInitializedException();
        }

        private static void FixCapstoneLib()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;
            
            //Capstone is super stupid and randomly fails to load on non-windows platforms. Fix it.
            var runningFrom = AppContext.BaseDirectory;
            var capstonePath = Path.Combine(runningFrom, "Gee.External.Capstone.dll");

            if (!File.Exists(capstonePath))
            {

                Logger.InfoNewline("Detected that Capstone's Managed assembly is missing. Attempting to copy the windows one...");
                var fallbackPath = Path.Combine(runningFrom, "runtimes", "win-x64", "lib", "netstandard2.0", "Gee.External.Capstone.dll");

                if (!File.Exists(fallbackPath))
                {
                    Logger.WarnNewline($"Couldn't find it at {fallbackPath}. Your application will probably now throw an exception due to it being missing.");
                    return;
                }

                File.Copy(fallbackPath, capstonePath);
            }

            var loaded = Assembly.LoadFile(capstonePath);
            Logger.InfoNewline("Loaded capstone: " + loaded.FullName);

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                if (args.Name == loaded.FullName)
                    return loaded;

                return null;
            };
        }
    }
}