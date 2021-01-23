//Disabled because it's slow as hell
// #define DUMP_PACKAGE_SUCCESS_DATA

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using CommandLine;
using Cpp2IL.Analysis;
using LibCpp2IL;
using LibCpp2IL.PE;
using Mono.Cecil;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace Cpp2IL
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    internal class Program
    {
        [SuppressMessage("ReSharper", "NotNullMemberIsNotInitialized")]
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        internal class Options
        {
            [Option("game-path", Required = true, HelpText = "Specify path to the game folder (containing the exe)")]
            public string GamePath { get; set; }

            [Option("exe-name", Required = false, HelpText = "Specify an override for the unity executable name in case the auto-detection doesn't work.")]
            public string? ExeName { get; set; }

            [Option("skip-analysis", Required = false, HelpText = "Skip the analysis section and stop once DummyDLLs have been generated.")]
            public bool SkipAnalysis { get; set; }

            [Option("skip-metadata-txts", Required = false, HelpText = "Skip the generation of [classname]_metadata.txt files.")]
            public bool SkipMetadataTextFiles { get; set; }

            [Option("disable-registration-prompts", Required = false, HelpText = "Disable the prompt if Code or Metadata Registration function addresses cannot be located.")]
            public bool DisableRegistrationPrompts { get; set; }
        }

        private static readonly string[] BlacklistedExecutableFilenames =
        {
            "UnityCrashHandler.exe",
            "UnityCrashHandler64.exe",
            "install.exe",
            "MelonLoader.Installer.exe"
        };

        private static List<AssemblyDefinition> Assemblies = new List<AssemblyDefinition>();
        internal static Options? CommandLineOptions;

        public static int Main(string[] args)
        {
            Console.WriteLine("===Cpp2IL by Samboy063===");
            Console.WriteLine("A Tool to Reverse Unity's \"il2cpp\" Build Process.");
            Console.WriteLine("Running on " + Environment.OSVersion.Platform);

            #region Command Line Parsing

            CommandLineOptions = null;
            Parser.Default.ParseArguments<Options>(args).WithParsed(options => { CommandLineOptions = options; });

            if (CommandLineOptions == null)
            {
                Console.WriteLine("Invalid command line. Exiting.");
                return 1;
            }

            var baseGamePath = CommandLineOptions.GamePath;

            Console.WriteLine("Using path: " + baseGamePath);

            if (!Directory.Exists(baseGamePath))
            {
                Console.WriteLine("Specified game-path does not exist: " + baseGamePath);
                return 2;
            }

            var assemblyPath = Path.Combine(baseGamePath, "GameAssembly.dll");
            var exeName = Path.GetFileNameWithoutExtension(Directory.GetFiles(baseGamePath)
                .First(f => f.EndsWith(".exe") && !BlacklistedExecutableFilenames.Any(bl => f.EndsWith(bl))));

            if (CommandLineOptions.ExeName != null)
            {
                exeName = CommandLineOptions.ExeName;
                Console.WriteLine($"Using OVERRIDDEN game name: {exeName}");
            }
            else
            {
                Console.WriteLine($"Auto-detected game name: {exeName}");
            }

            var unityPlayerPath = Path.Combine(baseGamePath, $"{exeName}.exe");
            var metadataPath = Path.Combine(baseGamePath, $"{exeName}_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

            if (!File.Exists(assemblyPath) || !File.Exists(unityPlayerPath) || !File.Exists(metadataPath))
            {
                Console.WriteLine("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
                                  $"\t{assemblyPath}\n" +
                                  $"\t{unityPlayerPath}\n" +
                                  $"\t{metadataPath}\n");

                return 2;
            }

            #endregion

            Console.WriteLine($"Located game EXE: {unityPlayerPath}");
            Console.WriteLine($"Located global-metadata: {metadataPath}");

            #region Unity Version Determination

            Console.WriteLine("\nAttempting to determine Unity version...");

            int[] unityVerUseful;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var unityVer = FileVersionInfo.GetVersionInfo(unityPlayerPath);

                unityVerUseful = new[] {unityVer.FileMajorPart, unityVer.FileMinorPart, unityVer.FileBuildPart};
            }
            else
            {
                //Globalgamemanagers
                var globalgamemanagersPath = Path.Combine(baseGamePath, $"{exeName}_Data", "globalgamemanagers");
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
                Console.WriteLine("Read version string from globalgamemanagers: " + unityVer);
                unityVerUseful = unityVer.Split(".").Select(int.Parse).ToArray();
            }

            Console.WriteLine("This game is built with Unity version " + string.Join(".", unityVerUseful));

            if (unityVerUseful[0] <= 4)
            {
                Console.WriteLine("Unable to determine a valid unity version. Aborting.");
                return 1;
            }

            #endregion

            //Set this flag from command line options
            LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput = !CommandLineOptions.DisableRegistrationPrompts;

            //Disable Method Ptr Mapping and Global Resolving if skipping analysis
            LibCpp2IlMain.Settings.DisableMethodPointerMapping = LibCpp2IlMain.Settings.DisableGlobalResolving = CommandLineOptions.SkipAnalysis;

            if (!LibCpp2IlMain.LoadFromFile(assemblyPath, metadataPath, unityVerUseful))
            {
                Console.WriteLine("Initialization with LibCpp2IL failed.");
                return 1;
            }

            //Dump DLLs

            #region Assembly Generation

            var resolver = new RegistryAssemblyResolver();
            var moduleParams = new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                AssemblyResolver = resolver,
                MetadataResolver = new MetadataResolver(resolver)
            };


            Console.WriteLine("Building assemblies...");
            Console.WriteLine("\tPass 1: Creating empty types...");

            Assemblies = AssemblyBuilder.CreateAssemblies(LibCpp2IlMain.TheMetadata!, resolver, moduleParams);

            Utils.BuildPrimitiveMappings();

            Console.WriteLine("\tPass 2: Setting parents and handling inheritance...");

            //Stateful method, no return value
            AssemblyBuilder.ConfigureHierarchy(LibCpp2IlMain.TheMetadata, LibCpp2IlMain.ThePe!);

            Console.WriteLine("\tPass 3: Populating types...");

            var methods = new List<(TypeDefinition type, List<CppMethodData> methods)>();

            //Create out dirs if needed
            var outputPath = Path.GetFullPath("cpp2il_out");
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            var methodOutputDir = Path.Combine(outputPath, "types");
            if (!(CommandLineOptions.SkipAnalysis && CommandLineOptions.SkipMetadataTextFiles) && !Directory.Exists(methodOutputDir))
                Directory.CreateDirectory(methodOutputDir);

            for (var imageIndex = 0; imageIndex < LibCpp2IlMain.TheMetadata.assemblyDefinitions.Length; imageIndex++)
            {
                var imageDef = LibCpp2IlMain.TheMetadata.assemblyDefinitions[imageIndex];

                Console.WriteLine($"\t\tPopulating {imageDef.typeCount} types in assembly {imageIndex + 1} of {LibCpp2IlMain.TheMetadata.assemblyDefinitions.Length}: {imageDef.Name}...");

                var assemblySpecificPath = Path.Combine(methodOutputDir, imageDef.Name.Replace(".dll", ""));
                if (!(CommandLineOptions.SkipMetadataTextFiles && CommandLineOptions.SkipAnalysis) && !Directory.Exists(assemblySpecificPath))
                    Directory.CreateDirectory(assemblySpecificPath);

                methods.AddRange(AssemblyBuilder.ProcessAssemblyTypes(LibCpp2IlMain.TheMetadata, LibCpp2IlMain.ThePe, imageDef));
            }

            //Invert dict for CppToMono
            SharedState.CppToMonoTypeDefs = SharedState.MonoToCppTypeDefs.ToDictionary(i => i.Value, i => i.Key);

            Console.WriteLine("\tPass 4: Handling SerializeFields...");
            //Add serializefield to monobehaviors

            #region SerializeFields

            var unityEngineAssembly = Assemblies.Find(x => x.MainModule.Types.Any(t => t.Namespace == "UnityEngine" && t.Name == "SerializeField"));
            if (unityEngineAssembly != null)
            {
                var serializeFieldMethod = unityEngineAssembly.MainModule.Types.First(x => x.Name == "SerializeField").Methods.First();
                foreach (var imageDef in LibCpp2IlMain.TheMetadata.assemblyDefinitions)
                {
                    var lastTypeIndex = imageDef.firstTypeIndex + imageDef.typeCount;
                    for (var typeIndex = imageDef.firstTypeIndex; typeIndex < lastTypeIndex; typeIndex++)
                    {
                        var typeDef = LibCpp2IlMain.TheMetadata.typeDefs[typeIndex];
                        var typeDefinition = SharedState.TypeDefsByIndex[typeIndex];

                        //Fields
                        var lastFieldIdx = typeDef.firstFieldIdx + typeDef.field_count;
                        for (var fieldIdx = typeDef.firstFieldIdx; fieldIdx < lastFieldIdx; ++fieldIdx)
                        {
                            var fieldDef = LibCpp2IlMain.TheMetadata.fieldDefs[fieldIdx];
                            var fieldName = LibCpp2IlMain.TheMetadata.GetStringFromIndex(fieldDef.nameIndex);
                            var fieldDefinition = typeDefinition.Fields.First(x => x.Name == fieldName);

                            //Get attributes and look for the serialize field attribute.
                            var attributeTypeRange = LibCpp2IlMain.TheMetadata.GetCustomAttributeIndex(imageDef, fieldDef.customAttributeIndex, fieldDef.token);

                            if (attributeTypeRange == null) continue;

                            for (var attributeIdxIdx = 0; attributeIdxIdx < attributeTypeRange.count; attributeIdxIdx++)
                            {
                                var attributeTypeIndex = LibCpp2IlMain.TheMetadata.attributeTypes[attributeTypeRange.start + attributeIdxIdx];
                                var attributeType = LibCpp2IlMain.ThePe.types[attributeTypeIndex];
                                if (attributeType.type != Il2CppTypeEnum.IL2CPP_TYPE_CLASS) continue;
                                var cppAttribType = LibCpp2IlMain.TheMetadata.typeDefs[attributeType.data.classIndex];
                                var attributeName = LibCpp2IlMain.TheMetadata.GetStringFromIndex(cppAttribType.nameIndex);
                                if (attributeName != "SerializeField") continue;
                                var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(serializeFieldMethod));
                                fieldDefinition.CustomAttributes.Add(customAttribute);
                            }
                        }
                    }
                }
            }

            #endregion

            KeyFunctionAddresses keyFunctionAddresses = null;
            if (!CommandLineOptions.SkipAnalysis)
            {
                Console.WriteLine("\tPass 5: Locating Globals...");

                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.TypeRefs.Count} type globals");
                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.MethodRefs.Count} method globals");
                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.FieldRefs.Count} field globals");
                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.Literals.Count} string literals");

                //TODO: Don't do this. Rework everything to use the API surface.
                SharedState.Globals.AddRange(LibCpp2IlGlobalMapper.TypeRefs);
                SharedState.Globals.AddRange(LibCpp2IlGlobalMapper.MethodRefs);
                SharedState.Globals.AddRange(LibCpp2IlGlobalMapper.FieldRefs);
                SharedState.Globals.AddRange(LibCpp2IlGlobalMapper.Literals);

                foreach (var globalIdentifier in SharedState.Globals)
                    SharedState.GlobalsByOffset[globalIdentifier.Offset] = globalIdentifier;

                Console.WriteLine("\tPass 6: Looking for key functions...");

                //This part involves decompiling known functions to search for other function calls

                Disassembler.Translator.IncludeAddress = true;
                Disassembler.Translator.IncludeBinary = true;

                keyFunctionAddresses = KeyFunctionAddresses.Find(methods, LibCpp2IlMain.ThePe);
            }

            #endregion

            Console.WriteLine("Saving Header DLLs to " + outputPath + "...");

            SaveHeaderDLLs(outputPath);

            if (CommandLineOptions.SkipAnalysis) return 0; //Success

            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            var methodTaintDict = DoAssemblyCSharpAnalysis(methodOutputDir, methods, keyFunctionAddresses!, out var total);

            Console.WriteLine("Breakdown By Taint Reason:");
            foreach (var reason in Enum.GetValues(typeof(AsmDumper.TaintReason)))
            {
                var count = (decimal) methodTaintDict.Values.Count(v => v == (AsmDumper.TaintReason) reason);
                Console.WriteLine($"{reason}: {count} (about {Math.Round(count * 100 / total, 1)}%)");
            }

            var summary = new StringBuilder();
            foreach (var (methodName, taintReason) in methodTaintDict)
            {
                summary.Append('\t')
                    .Append(methodName)
                    .Append(Utils.Repeat(" ", 250 - methodName.Length))
                    .Append(taintReason)
                    .Append(" (")
                    .Append((int) taintReason)
                    .Append(')')
                    .Append('\n');
            }
            
            File.WriteAllText(Path.Combine(outputPath, "method_statuses.txt"), summary.ToString());
            Console.WriteLine($"Wrote file: {Path.Combine(outputPath, "method_statuses.txt")}");

            return 0;
        }

        private static void SaveHeaderDLLs(string outputPath)
        {
            foreach (var assembly in Assemblies)
            {
                var dllPath = Path.Combine(outputPath, assembly.MainModule.Name);

                //Remove NetCore Dependencies 
                var reference = assembly.MainModule.AssemblyReferences.FirstOrDefault(a => a.Name == "System.Private.CoreLib");
                if (reference != null)
                    assembly.MainModule.AssemblyReferences.Remove(reference);

                assembly.Write(dllPath);
            }
        }

        private static ConcurrentDictionary<string, AsmDumper.TaintReason> DoAssemblyCSharpAnalysis(string methodOutputDir, List<(TypeDefinition type, List<CppMethodData> methods)> methods, KeyFunctionAddresses keyFunctionAddresses, out int total)
        {
            var assembly = Assemblies.Find(a => a.Name.Name == "Assembly-CSharp");

            Console.WriteLine("Dumping method bytes to " + methodOutputDir);
            Directory.CreateDirectory(Path.Combine(methodOutputDir, assembly.Name.Name));

            var allUsedMnemonics = new List<ud_mnemonic_code>();

            var counter = 0;
            var toProcess = methods.Where(tuple => tuple.type.Module.Assembly == assembly).ToList();

            //Sort alphabetically by type.
            toProcess.Sort((a, b) => String.Compare(a.type.FullName, b.type.FullName, StringComparison.Ordinal));
            var thresholds = new[] {10, 20, 30, 40, 50, 60, 70, 80, 90, 100}.ToList();
            var nextThreshold = thresholds.First();

            var successfullyProcessed = 0;
            var failedProcess = 0;

            var startTime = DateTime.Now;

            var methodTaintDict = new ConcurrentDictionary<string, AsmDumper.TaintReason>();

            thresholds.RemoveAt(0);


            Action<(TypeDefinition type, List<CppMethodData> methods)> action = tuple =>
            {
                var (type, methodData) = tuple;
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
                            Console.WriteLine($"{nextThreshold}% ({counter} classes in {Math.Round(elapsedSoFar.TotalSeconds)} sec, ~{Math.Round(rate)} classes / sec, {remaining} classes remaining, approx {Math.Round(remaining / rate + 5)} sec remaining)");
                            nextThreshold = thresholds.First();
                            thresholds.RemoveAt(0);
                        }
                    }
                }

                // Console.WriteLine($"\t-Dumping methods in type {counter}/{methodBytes.Count}: {type.Key}");
                try
                {
                    var filename = Path.Combine(methodOutputDir, assembly.Name.Name, type.Name.Replace("<", "_").Replace(">", "_").Replace("|", "_") + "_methods.txt");
                    var typeDump = new StringBuilder("Type: " + type.Name + "\n\n");

                    foreach (var method in methodData)
                    {
                        var methodStart = method.MethodOffsetRam;

                        if (methodStart == 0) continue;

                        var methodDefinition = SharedState.MethodsByIndex[method.MethodId];

                        var taintResult = new AsmDumper(methodDefinition, method, methodStart, keyFunctionAddresses!, LibCpp2IlMain.ThePe)
                            .AnalyzeMethod(typeDump, ref allUsedMnemonics);

                        var key = new StringBuilder();

                        key.Append(methodDefinition.DeclaringType.FullName).Append("::").Append(methodDefinition.Name);

                        methodDefinition.MethodSignatureFullName(key);

                        methodTaintDict[key.ToString()] = taintResult;

                        if (taintResult != AsmDumper.TaintReason.UNTAINTED)
                            Interlocked.Increment(ref failedProcess);
                        else
                            Interlocked.Increment(ref successfullyProcessed);
                    }

                    lock (type)
                        File.WriteAllText(filename, typeDump.ToString());
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to dump methods for type " + type.Name + " " + e);
                }
            };


            var parallel = false;

            if (parallel)
                toProcess.AsParallel().ForAll(action);
            else
                toProcess.ForEach(action);


            total = successfullyProcessed + failedProcess;

            var elapsed = DateTime.Now - startTime;
            Console.WriteLine($"Finished method processing in {elapsed.Ticks} ticks (about {Math.Round(elapsed.TotalSeconds, 1)} seconds), at an overall rate of about {Math.Round(toProcess.Count / elapsed.TotalSeconds)} methods/sec");
            Console.WriteLine($"Processed {total} methods, {successfullyProcessed} ({Math.Round(successfullyProcessed * 100.0 / total, 2)}%) successfully, {failedProcess} ({Math.Round(failedProcess * 100.0 / total, 2)}%) with errors.");
            return methodTaintDict;
        }

#if DUMP_PACKAGE_SUCCESS_DATA
        private static string GetPackageName(string fullName)
        {
            if (fullName.Contains("::"))
                fullName = fullName.Substring(0, fullName.IndexOf("::", StringComparison.Ordinal));

            var split = fullName.Split('.').ToList();
            var type = split.Last();
            split.Remove(type);

            return string.Join(".", split);
        }
#endif
    }
}