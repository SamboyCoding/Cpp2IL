using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
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
        public static float MetadataVersion = 24f;

        private static List<AssemblyDefinition> Assemblies = new List<AssemblyDefinition>();

        public static void PrintUsage()
        {
            Console.WriteLine("Usage: Cpp2IL <path to game folder>");
        }

        public static void Main(string[] args)
        {
            Console.WriteLine("===Cpp2IL by Samboy063===");
            Console.WriteLine("A Tool to Reverse Unity's \"il2cpp\" Build Process.");
            Console.WriteLine("Running on " + Environment.OSVersion.Platform);

            string loc;
            if (Environment.OSVersion.Platform == PlatformID.Win32Windows || Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                loc = Registry.GetValue(
                    "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 1020340",
                    "InstallLocation", null) as string;
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                // $HOME/.local/share/Steam/steamapps/common/Audica
                loc = Environment.GetEnvironmentVariable("HOME") + "/.local/share/Steam/steamapps/common/Audica";
            }
            else 
            {
                loc = null;
            }

            if (args.Length != 1 && loc == null)
            {
                Console.WriteLine(
                    "Couldn't auto-detect Audica installation folder (via steam), and you didn't tell me where it is.");
                PrintUsage();
                return;
            }

            var baseGamePath = args.Length > 0 ? args[0] : loc;

            Console.WriteLine("Using path: " + baseGamePath);

            if (!Directory.Exists(baseGamePath))
            {
                Console.WriteLine("Specified path does not exist: " + baseGamePath);
                PrintUsage();
                return;
            }

            var assemblyPath = Path.Combine(baseGamePath, "GameAssembly.dll");
            var exeName = Directory.GetFiles(baseGamePath).First(f => f.EndsWith(".exe") && !f.Contains("UnityCrashHandler")).Replace(".exe", "");
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

            var unityVer = FileVersionInfo.GetVersionInfo(unityPlayerPath).FileVersion;
            var split = unityVer.Split(new[] {'.'}, StringSplitOptions.RemoveEmptyEntries);
            var unityVerUseful = split.SubArray(0, 2).Select(int.Parse).ToArray();

            Console.WriteLine("This game is built with Unity version " + string.Join(".", unityVerUseful));

            Console.WriteLine("Reading metadata...");
            var metadata = Il2CppMetadata.ReadFrom(metadataPath, unityVerUseful);

            Console.WriteLine("Reading binary / game assembly...");
            var PEBytes = File.ReadAllBytes(assemblyPath);
            
            Console.WriteLine($"\t-Initializing MemoryStream of {PEBytes.Length} bytes, parsing sections, and initializing with auto+ mode.");

            var theDll = new PE.PE(new MemoryStream(PEBytes, 0, PEBytes.Length, false, true), metadata.maxMetadataUsages);
            if (!theDll.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length))
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

            Assemblies = AssemblyBuilder.CreateAssemblies(metadata, resolver, moduleParams);

            Console.WriteLine("\tPass 2: Setting parents and handling inheritance...");

            //Stateful method, no return value
            AssemblyBuilder.ConfigureHierarchy(metadata, theDll);

            Console.WriteLine("\tPass 3: Handling Fields, methods, and properties (THIS MAY TAKE A WHILE)...");

            var methods = new List<(TypeDefinition type, List<CppMethodData> methods)>();
            for (var imageIndex = 0; imageIndex < metadata.assemblyDefinitions.Length; imageIndex++)
            {
                Console.WriteLine($"\t\tProcessing DLL {imageIndex + 1} of {metadata.assemblyDefinitions.Length}...");
                methods.AddRange(AssemblyBuilder.ProcessAssemblyTypes(metadata, theDll, metadata.assemblyDefinitions[imageIndex]));
            }

            Console.WriteLine("\tPass 4: Handling SerializeFields...");
            //Add serializefield to monobehaviors

            #region SerializeFields

            var unityEngineAssembly = Assemblies.Find(x => x.MainModule.Types.Any(t => t.Namespace == "UnityEngine" && t.Name == "SerializeField"));
            if (unityEngineAssembly != null)
            {
                var serializeFieldMethod = unityEngineAssembly.MainModule.Types.First(x => x.Name == "SerializeField").Methods.First();
                foreach (var imageDef in metadata.assemblyDefinitions)
                {
                    var lastTypeIndex = imageDef.firstTypeIndex + imageDef.typeCount;
                    for (var typeIndex = imageDef.firstTypeIndex; typeIndex < lastTypeIndex; typeIndex++)
                    {
                        var typeDef = metadata.typeDefs[typeIndex];
                        var typeDefinition = SharedState.TypeDefsByAddress[typeIndex];

                        //Fields
                        var lastFieldIdx = typeDef.firstFieldIdx + typeDef.field_count;
                        for (var fieldIdx = typeDef.firstFieldIdx; fieldIdx < lastFieldIdx; ++fieldIdx)
                        {
                            var fieldDef = metadata.fieldDefs[fieldIdx];
                            var fieldName = metadata.GetStringFromIndex(fieldDef.nameIndex);
                            var fieldDefinition = typeDefinition.Fields.First(x => x.Name == fieldName);

                            //Get attributes and look for the serialize field attribute.
                            var attributeIndex = metadata.GetCustomAttributeIndex(imageDef, fieldDef.customAttributeIndex, fieldDef.token);
                            if (attributeIndex < 0) continue;
                            var attributeTypeRange = metadata.attributeTypeRanges[attributeIndex];
                            for (var attributeIdxIdx = 0; attributeIdxIdx < attributeTypeRange.count; attributeIdxIdx++)
                            {
                                var attributeTypeIndex = metadata.attributeTypes[attributeTypeRange.start + attributeIdxIdx];
                                var attributeType = theDll.types[attributeTypeIndex];
                                if (attributeType.type != Il2CppTypeEnum.IL2CPP_TYPE_CLASS) continue;
                                var cppAttribType = metadata.typeDefs[attributeType.data.classIndex];
                                var attributeName = metadata.GetStringFromIndex(cppAttribType.nameIndex);
                                if (attributeName != "SerializeField") continue;
                                var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(serializeFieldMethod));
                                fieldDefinition.CustomAttributes.Add(customAttribute);
                            }
                        }
                    }
                }
            }

            #endregion

            Console.WriteLine("\tPass 5: Locating Globals...");

            var globals = AssemblyBuilder.MapGlobalIdentifiers(metadata, theDll);

            Console.WriteLine($"\t\tFound {globals.Count(g => g.IdentifierType == AssemblyBuilder.GlobalIdentifier.Type.TYPE)} type globals");
            Console.WriteLine($"\t\tFound {globals.Count(g => g.IdentifierType == AssemblyBuilder.GlobalIdentifier.Type.METHOD)} method globals");
            Console.WriteLine($"\t\tFound {globals.Count(g => g.IdentifierType == AssemblyBuilder.GlobalIdentifier.Type.FIELD)} field globals");
            Console.WriteLine($"\t\tFound {globals.Count(g => g.IdentifierType == AssemblyBuilder.GlobalIdentifier.Type.LITERAL)} string literals");

            SharedState.Globals.AddRange(globals);
            
            foreach (var globalIdentifier in globals)
                SharedState.GlobalsDict[globalIdentifier.Offset] = globalIdentifier;

            Console.WriteLine("\tPass 6: Looking for key functions...");

            //This part involves decompiling known functions to search for other function calls

            Disassembler.Translator.IncludeAddress = true;
            Disassembler.Translator.IncludeBinary = true;

            var keyFunctionAddresses = KeyFunctionAddresses.Find(methods, theDll);

            #endregion

            Utils.BuildPrimitiveMappings();
            
            var outputPath = Path.GetFullPath("cpp2il_out");
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            var methodOutputDir = Path.Combine(outputPath, "types");
            if (!Directory.Exists(methodOutputDir))
                Directory.CreateDirectory(methodOutputDir);

            Console.WriteLine("Saving Header DLLs to " + outputPath + "...");

            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            foreach (var assembly in Assemblies)
            {
                var dllPath = Path.Combine(outputPath, assembly.MainModule.Name);

                assembly.Write(dllPath);

                if (assembly.Name.Name != "Assembly-CSharp") continue;

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
                            var filename = Path.Combine(methodOutputDir, assembly.Name.Name, type.Name.Replace("<", "_").Replace(">", "_") + "_methods.txt");
                            var typeDump = new StringBuilder("Type: " + type.Name + "\n\n");

                            foreach (var method in methodData)
                            {
                                var methodDef = metadata.methodDefs[method.MethodId];
                                var methodStart = method.MethodOffsetRam;
                                var methodDefinition = SharedState.MethodsByIndex[method.MethodId];

                                var taintResult = new AsmDumper(methodDefinition, method, methodStart, keyFunctionAddresses, theDll)
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
                    summary.Append(keyValuePair.Key)
                        .Append(Utils.Repeat(" ", 250 - keyValuePair.Key.Length))
                        .Append(keyValuePair.Value)
                        .Append(" (")
                        .Append((int) keyValuePair.Value)
                        .Append(")")
                        .Append("\n");
                }
                
                File.WriteAllText(Path.Combine(outputPath, "method_statuses.txt"), summary.ToString());
                Console.WriteLine($"Wrote file: {Path.Combine(outputPath, "method_statuses.txt")}");
                
                // Console.WriteLine("Assembly uses " + allUsedMnemonics.Count + " mnemonics");
            }
            
            Console.WriteLine("[Finished. Press enter to exit]");
            Console.ReadLine();
        }


        #region Assembly Generation Helper Functions

        #endregion
    }
}