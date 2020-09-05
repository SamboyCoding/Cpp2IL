using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using CommandLine;
using Cpp2IL.Analysis;
using Cpp2IL.Metadata;
using Cpp2IL.PE;
using Microsoft.Win32;
using Mono.Cecil;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace Cpp2IL
{
    internal class Program
    {
        internal class Options
        {
            [Option("game-path", Required = true, HelpText = "Specify path to the game folder (containing the exe)")]
            public string GamePath { get; set; }
            
            [Option("exe-name", Required = false, HelpText = "Specify an override for the unity executable name in case the auto-detection doesn't work.")]
            public string ExeName { get; set; }
            
            [Option("skip-analysis", Required = false, HelpText = "Skip the analysis section and stop once DummyDLLs have been generated.")]
            public bool SkipAnalysis { get; set; }
            
            [Option("skip-metadata-txts", Required = false, HelpText = "Skip the generation of [classname]_metadata.txt files.")]
            public bool SkipMetadataTextFiles { get; set; }
        }
        
        public static float MetadataVersion = 24f;

        private static readonly string[] blacklistedExecutableFilenames = {
            "UnityCrashHandler.exe",
            "UnityCrashHandler64.exe",
            "install.exe"
        };

        private static List<AssemblyDefinition> Assemblies = new List<AssemblyDefinition>();
        internal static Il2CppMetadata? Metadata;
        internal static PE.PE ThePE;
        internal static Options CommandLineOptions;

        public static void PrintUsage()
        {
            Console.WriteLine("Usage: Cpp2IL <path to game folder> [name of game exe]");
        }

        public static void Main(string[] args)
        {
            Console.WriteLine("===Cpp2IL by Samboy063===");
            Console.WriteLine("A Tool to Reverse Unity's \"il2cpp\" Build Process.");
            Console.WriteLine("Running on " + Environment.OSVersion.Platform);

            CommandLineOptions = null;
            Parser.Default.ParseArguments<Options>(args).WithParsed(options =>
            {
                CommandLineOptions = options;
            });

            if (CommandLineOptions == null)
            {
                return;
            }

            string loc;
            
            //TODO: No longer needed
            // if (Environment.OSVersion.Platform == PlatformID.Win32Windows || Environment.OSVersion.Platform == PlatformID.Win32NT)
            // {
            //     loc = Registry.GetValue(
            //         "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 1020340",
            //         "InstallLocation", null) as string;
            // }
            // else if (Environment.OSVersion.Platform == PlatformID.Unix)
            // {
            //     // $HOME/.local/share/Steam/steamapps/common/Audica
            //     loc = Environment.GetEnvironmentVariable("HOME") + "/.local/share/Steam/steamapps/common/Audica";
            // }
            // else
            // {
            //     loc = null;
            // }
            //
            // if (args.Length != 1 && loc == null)
            // {
            //     Console.WriteLine(
            //         "Couldn't auto-detect Audica installation folder (via steam), and you didn't tell me where it is.");
            //     PrintUsage();
            //     return;
            // }

            var baseGamePath = CommandLineOptions.GamePath;

            Console.WriteLine("Using path: " + baseGamePath);

            if (!Directory.Exists(baseGamePath))
            {
                Console.WriteLine("Specified path does not exist: " + baseGamePath);
                PrintUsage();
                return;
            }

            var assemblyPath = Path.Combine(baseGamePath, "GameAssembly.dll");
            var exeName = Path.GetFileNameWithoutExtension(Directory.GetFiles(baseGamePath)
                .First(f => f.EndsWith(".exe") && !blacklistedExecutableFilenames.Any(bl => f.EndsWith(bl))));
            
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
            var metadataPath = Path.Combine(baseGamePath, $"{exeName}_Data", "il2cpp_data", "Metadata",
                "global-metadata.dat");

            if (!File.Exists(assemblyPath) || !File.Exists(unityPlayerPath) || !File.Exists(metadataPath))
            {
                Console.WriteLine("Invalid path specified. Failed to find one of the following:\n" +
                                  $"\t{assemblyPath}\n" +
                                  $"\t{unityPlayerPath}\n" +
                                  $"\t{metadataPath}\n");
                PrintUsage();
                return;
            }
            
            Console.WriteLine($"Located game EXE: {unityPlayerPath}");
            Console.WriteLine($"Located global-metadata: {metadataPath}");
            
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
                unityVer = unityVer.Substring(0, unityVer.IndexOf("f"));
                Console.WriteLine("Read version string from globalgamemanagers: " + unityVer);
                unityVerUseful = unityVer.Split(".").Select(int.Parse).ToArray();
            }

            Console.WriteLine("This game is built with Unity version " + string.Join(".", unityVerUseful));

            if (unityVerUseful[0] <= 4)
            {
                Console.WriteLine("Unable to determine a valid unity version. Aborting.");
                return;
            }

            Console.WriteLine("Reading metadata...");
            Metadata = Il2CppMetadata.ReadFrom(metadataPath, unityVerUseful);

            Console.WriteLine($"Reading binary / game assembly file {assemblyPath}...");
            var PEBytes = File.ReadAllBytes(assemblyPath);

            Console.WriteLine($"\t-Initializing MemoryStream of {PEBytes.Length} bytes, parsing sections, and initializing with auto+ mode.");

            ThePE = new PE.PE(new MemoryStream(PEBytes, 0, PEBytes.Length, false, true), Metadata.maxMetadataUsages);
            if (!ThePE.PlusSearch(Metadata.methodDefs.Count(x => x.methodIndex >= 0), Metadata.typeDefs.Length))
            {
                Console.WriteLine("Initialize failed. Aborting.");
                return;
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
            Console.WriteLine("\tPass 1: Creating types...");

            Assemblies = AssemblyBuilder.CreateAssemblies(Metadata, resolver, moduleParams);

            Console.WriteLine("\tPass 2: Setting parents and handling inheritance...");

            //Stateful method, no return value
            AssemblyBuilder.ConfigureHierarchy(Metadata, ThePE);

            Console.WriteLine("\tPass 3: Handling Fields, methods, and properties (THIS MAY TAKE A WHILE)...");

            var methods = new List<(TypeDefinition type, List<CppMethodData> methods)>();
            for (var imageIndex = 0; imageIndex < Metadata.assemblyDefinitions.Length; imageIndex++)
            {
                Console.WriteLine($"\t\tProcessing DLL {imageIndex + 1} of {Metadata.assemblyDefinitions.Length}...");
                methods.AddRange(AssemblyBuilder.ProcessAssemblyTypes(Metadata, ThePE, Metadata.assemblyDefinitions[imageIndex]));
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
                foreach (var imageDef in Metadata.assemblyDefinitions)
                {
                    var lastTypeIndex = imageDef.firstTypeIndex + imageDef.typeCount;
                    for (var typeIndex = imageDef.firstTypeIndex; typeIndex < lastTypeIndex; typeIndex++)
                    {
                        var typeDef = Metadata.typeDefs[typeIndex];
                        var typeDefinition = SharedState.TypeDefsByIndex[typeIndex];

                        //Fields
                        var lastFieldIdx = typeDef.firstFieldIdx + typeDef.field_count;
                        for (var fieldIdx = typeDef.firstFieldIdx; fieldIdx < lastFieldIdx; ++fieldIdx)
                        {
                            var fieldDef = Metadata.fieldDefs[fieldIdx];
                            var fieldName = Metadata.GetStringFromIndex(fieldDef.nameIndex);
                            var fieldDefinition = typeDefinition.Fields.First(x => x.Name == fieldName);

                            //Get attributes and look for the serialize field attribute.
                            var attributeIndex = Metadata.GetCustomAttributeIndex(imageDef, fieldDef.customAttributeIndex, fieldDef.token);
                            if (attributeIndex < 0) continue;
                            var attributeTypeRange = Metadata.attributeTypeRanges[attributeIndex];
                            for (var attributeIdxIdx = 0; attributeIdxIdx < attributeTypeRange.count; attributeIdxIdx++)
                            {
                                var attributeTypeIndex = Metadata.attributeTypes[attributeTypeRange.start + attributeIdxIdx];
                                var attributeType = ThePE.types[attributeTypeIndex];
                                if (attributeType.type != Il2CppTypeEnum.IL2CPP_TYPE_CLASS) continue;
                                var cppAttribType = Metadata.typeDefs[attributeType.data.classIndex];
                                var attributeName = Metadata.GetStringFromIndex(cppAttribType.nameIndex);
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

                var globals = AssemblyBuilder.MapGlobalIdentifiers(Metadata, ThePE);

                Console.WriteLine($"\t\tFound {globals.Count(g => g.IdentifierType == GlobalIdentifier.Type.TYPE)} type globals");
                Console.WriteLine($"\t\tFound {globals.Count(g => g.IdentifierType == GlobalIdentifier.Type.METHOD)} method globals");
                Console.WriteLine($"\t\tFound {globals.Count(g => g.IdentifierType == GlobalIdentifier.Type.FIELD)} field globals");
                Console.WriteLine($"\t\tFound {globals.Count(g => g.IdentifierType == GlobalIdentifier.Type.LITERAL)} string literals");

                SharedState.Globals.AddRange(globals);

                foreach (var globalIdentifier in globals)
                    SharedState.GlobalsDict[globalIdentifier.Offset] = globalIdentifier;

                Console.WriteLine("\tPass 6: Looking for key functions...");

                //This part involves decompiling known functions to search for other function calls

                Disassembler.Translator.IncludeAddress = true;
                Disassembler.Translator.IncludeBinary = true;

                keyFunctionAddresses = KeyFunctionAddresses.Find(methods, ThePE);
            }

            #endregion

            Utils.BuildPrimitiveMappings();

            var outputPath = Path.GetFullPath("cpp2il_out");
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            var methodOutputDir = Path.Combine(outputPath, "types");
            if (!CommandLineOptions.SkipAnalysis && !Directory.Exists(methodOutputDir))
                Directory.CreateDirectory(methodOutputDir);

            Console.WriteLine("Saving Header DLLs to " + outputPath + "...");

            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            foreach (var assembly in Assemblies)
            {
                var dllPath = Path.Combine(outputPath, assembly.MainModule.Name);

                assembly.Write(dllPath);

                if (assembly.Name.Name != "Assembly-CSharp" || CommandLineOptions.SkipAnalysis) continue;

                Console.WriteLine("Dumping method bytes to " + methodOutputDir);
                Directory.CreateDirectory(Path.Combine(methodOutputDir, assembly.Name.Name));
                //Write methods

                var imageIndex = Assemblies.IndexOf(assembly);
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
                toProcess
                    .AsParallel()
                    .ForAll(tuple =>
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
                                var methodDef = Metadata.methodDefs[method.MethodId];
                                var methodStart = method.MethodOffsetRam;
                                var methodDefinition = SharedState.MethodsByIndex[method.MethodId];

                                var taintResult = new AsmDumper(methodDefinition, method, methodStart, keyFunctionAddresses, ThePE)
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
                    });

                var total = successfullyProcessed + failedProcess;

                var elapsed = DateTime.Now - startTime;
                Console.WriteLine($"Finished method processing in {elapsed.Ticks} ticks (about {Math.Round(elapsed.TotalSeconds, 1)} seconds), at an overall rate of about {Math.Round(toProcess.Count / elapsed.TotalSeconds)} methods/sec");
                Console.WriteLine($"Processed {total} methods, {successfullyProcessed} ({Math.Round(successfullyProcessed * 100.0 / total, 2)}%) successfully, {failedProcess} ({Math.Round(failedProcess * 100.0 / total, 2)}%) with errors.");

                Console.WriteLine("Breakdown By Taint Reason:");
                foreach (var reason in Enum.GetValues(typeof(AsmDumper.TaintReason)))
                {
                    var count = (decimal) methodTaintDict.Values.Count(v => v == (AsmDumper.TaintReason) reason);
                    Console.WriteLine($"{reason}: {count} (about {Math.Round(count * 100 / total, 1)}%)");
                }

                var summary = new StringBuilder();
                foreach (var keyValuePair in methodTaintDict)
                {
                    summary.Append("\t")
                        .Append(keyValuePair.Key)
                        .Append(Utils.Repeat(" ", 250 - keyValuePair.Key.Length))
                        .Append(keyValuePair.Value)
                        .Append(" (")
                        .Append((int) keyValuePair.Value)
                        .Append(")")
                        .Append("\n");
                }

                if (false)
                {
                    Console.WriteLine("By Package:");
                    var keys = methodTaintDict
                        .Select(kvp => kvp.Key)
                        .GroupBy(
                            GetPackageName,
                            className => className,
                            (packageName, keys) => new
                            {
                                package = packageName,
                                classes = keys.ToList()
                            })
                        .ToList();

                    foreach (var key in keys)
                    {
                        var resultLine = new StringBuilder();
                        var totalClassCount = key.classes.Count;
                        resultLine.Append($"\tIn package {key.package} ({totalClassCount} classes):   ");

                        foreach (var reason in Enum.GetValues(typeof(AsmDumper.TaintReason)))
                        {
                            var count = (decimal) methodTaintDict.Where(kvp => key.classes.Contains(kvp.Key)).Count(v => v.Value == (AsmDumper.TaintReason) reason);
                            resultLine.Append(reason).Append(":").Append(count).Append($" ({Math.Round(count * 100 / totalClassCount, 1)}%)   ");
                        }

                        Console.WriteLine(resultLine.ToString());
                    }
                }


                File.WriteAllText(Path.Combine(outputPath, "method_statuses.txt"), summary.ToString());
                Console.WriteLine($"Wrote file: {Path.Combine(outputPath, "method_statuses.txt")}");

                // Console.WriteLine("Assembly uses " + allUsedMnemonics.Count + " mnemonics");
            }

            // Console.WriteLine("[Finished. Press enter to exit]");
            // Console.ReadLine();
        }


        #region Assembly Generation Helper Functions

        #endregion

        private static string GetPackageName(string fullName)
        {
            if (fullName.Contains("::"))
                fullName = fullName.Substring(0, fullName.IndexOf("::", StringComparison.Ordinal));

            var split = fullName.Split('.').ToList();
            var type = split.Last();
            split.Remove(type);

            return string.Join(".", split);
        }
    }
}