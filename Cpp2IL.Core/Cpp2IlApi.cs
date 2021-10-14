using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Analysis.Actions.x86.Important;
using Cpp2IL.Core.Exceptions;
using LibCpp2IL;
using LibCpp2IL.Logging;
using Mono.Cecil;

namespace Cpp2IL.Core
{
    public static class Cpp2IlApi
    {
        public static List<AssemblyDefinition> GeneratedAssemblies => SharedState.AssemblyList.ToList(); //Shallow copy
        internal static bool IlContinueThroughErrors = false;

        public static AssemblyDefinition? GetAssemblyByName(string name) =>
            SharedState.AssemblyList.Find(a => a.Name.Name == name);

        public static int[] DetermineUnityVersion(string unityPlayerPath, string gameDataPath)
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
                version = GetVersionFromGlobalGameManagers(ggmBytes);
            }

            return version;
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

            fileStream.Seek(0x12, SeekOrigin.Begin);
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

            try
            {
                if (!LibCpp2IlMain.LoadFromFile(assemblyPath, metadataPath, unityVersion))
                    throw new Exception("Initialization with LibCpp2Il failed");
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

            try
            {
                if (!LibCpp2IlMain.Initialize(assemblyData, metadataData, unityVersion))
                    throw new Exception("Initialization with LibCpp2Il failed");
            }
            catch (Exception e)
            {
                throw new LibCpp2ILInitializationException("Fatal Exception initializing LibCpp2IL!", e);
            }
        }

        private static void ResetInternalState()
        {
            SharedState.Clear();

            Utils.Reset();

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
            Utils.BuildPrimitiveMappings();

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

            SharedState.AttributeGeneratorStarts = LibCpp2IlMain.Binary!.AllCustomAttributeGenerators.ToList();

            var enumerable = (IEnumerable<AssemblyDefinition>) SharedState.AssemblyList;

            if (parallel)
                enumerable = enumerable.AsParallel();

            enumerable.Select(def =>
            {
                RunAttributeRestorationForAssembly(def, keyFunctionAddresses);
                return true;
            }).ToList(); //Force full evaluation
        }

        public static void RunAttributeRestorationForAssembly(AssemblyDefinition assembly, BaseKeyFunctionAddresses? keyFunctionAddresses = null)
        {
            CheckLibInitialized();

            AttributeRestorer.ApplyCustomAttributesToAllTypesInAssembly(assembly, keyFunctionAddresses);
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

        public static void AnalyseAssembly(AnalysisLevel analysisLevel, AssemblyDefinition assembly, BaseKeyFunctionAddresses keyFunctionAddresses, string methodOutputDir, bool parallel, bool continueThroughErrors)
        {
            CheckLibInitialized();

            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            if (keyFunctionAddresses == null && LibCpp2IlMain.Binary!.InstructionSet is InstructionSet.X86_32 or InstructionSet.X86_64)
                throw new ArgumentNullException(nameof(keyFunctionAddresses));

            IlContinueThroughErrors = continueThroughErrors;
            
            AsmAnalyzerX86.FAILED_METHODS = 0;
            AsmAnalyzerX86.SUCCESSFUL_METHODS = 0;

            Logger.InfoNewline("Dumping method bytes to " + methodOutputDir, "Analyze");
            Directory.CreateDirectory(Path.Combine(methodOutputDir, assembly.Name.Name, "method_dumps"));

            var counter = 0;
            var toProcess = assembly.MainModule.Types.Where(t => t.Namespace != AssemblyPopulator.InjectedNamespaceName).ToList();
            toProcess.AddRange(toProcess.SelectMany(t => t.NestedTypes).ToList());
            //Sort alphabetically by type.
            toProcess.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
            var thresholds = new[] {10, 20, 30, 40, 50, 60, 70, 80, 90, 100}.ToList();
            var nextThreshold = thresholds.First();

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

                var fileSafeTypeName = type.Name;

                if (type.DeclaringType != null)
                    fileSafeTypeName = $"{type.DeclaringType.Name}--NestedType--{fileSafeTypeName}";
                
                fileSafeTypeName = fileSafeTypeName.Replace("<", "_").Replace(">", "_").Replace("|", "_");

                var methodDumpDir = Path.Combine(methodOutputDir, assembly.Name.Name, "method_dumps");

                if (!string.IsNullOrEmpty(type.Namespace))
                {
                    methodDumpDir = Path.Combine(new[] { methodDumpDir }.Concat(type.Namespace.Split('.')).ToArray());

                    if (!Directory.Exists(methodDumpDir))
                        Directory.CreateDirectory(methodDumpDir);
                }

                var filename = Path.Combine(methodDumpDir, $"{fileSafeTypeName}_methods.txt");
                var typeDump = new StringBuilder("Type: " + type.Name + "\n\n");

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

                        dumper.AnalyzeMethod();
                        dumper.RunPostProcessors();

                        switch (analysisLevel)
                        {
                            case AnalysisLevel.PRINT_ALL:
                                dumper.BuildMethodFunctionality();
                                typeDump.Append(dumper.GetFullDumpNoIL());
                                typeDump.Append(dumper.BuildILToString());
                                break;
                            case AnalysisLevel.SKIP_ASM:
                                dumper.BuildMethodFunctionality();
                                typeDump.Append(dumper.GetWordyFunctionality());
                                typeDump.Append(dumper.GetPseudocode());
                                typeDump.Append(dumper.BuildILToString());
                                break;
                            case AnalysisLevel.SKIP_ASM_AND_SYNOPSIS:
                                typeDump.Append(dumper.GetPseudocode());
                                typeDump.Append(dumper.BuildILToString());
                                break;
                            case AnalysisLevel.PSUEDOCODE_ONLY:
                                typeDump.Append(dumper.GetPseudocode());
                                break;
                            case AnalysisLevel.IL_ONLY:
                                typeDump.Append(dumper.BuildILToString());
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
                        Logger.WarnNewline("Failed to dump methods for type " + type.Name + " " + e, "Analyze");
                    }
                }

                lock (type) File.WriteAllText(filename, typeDump.ToString());
            }

            if (parallel)
                toProcess.AsParallel().ForAll(ProcessType);
            else
                toProcess.ForEach(ProcessType);

            var elapsed = DateTime.Now - startTime;
            Logger.InfoNewline($"Finished processing {numProcessed} methods in {elapsed.Ticks} ticks (about {Math.Round(elapsed.TotalSeconds, 1)} seconds), at an overall rate of about {Math.Round(toProcess.Count / elapsed.TotalSeconds)} methods/sec", "Analyze");

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
    }
}