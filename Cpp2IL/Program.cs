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
using Cpp2IL.Analysis.Actions;
using Cpp2IL.Analysis.Actions.Important;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using LibCpp2IL.PE;
using Mono.Cecil;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace Cpp2IL
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    internal class Program
    {
        private static List<AssemblyDefinition> Assemblies = new List<AssemblyDefinition>();

        public static int Main(string[] args)
        {
            Console.WriteLine("===Cpp2IL by Samboy063===");
            Console.WriteLine("A Tool to Reverse Unity's \"il2cpp\" Build Process.");
            Console.WriteLine("Running on " + Environment.OSVersion.Platform);

            var runtimeArgs = Cpp2IlTasks.GetRuntimeOptionsFromCommandLine(args);

            Cpp2IlTasks.InitializeLibCpp2Il(runtimeArgs);

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

            Assemblies = EmptyAssemblyBuilder.GetEmptyAssemblies(LibCpp2IlMain.TheMetadata!, moduleParams);
            Assemblies.ForEach(resolver.Register);

            Utils.BuildPrimitiveMappings();

            Console.WriteLine("\tPass 2: Setting parents and handling inheritance...");

            //Stateful method, no return value
            AssemblyPopulator.ConfigureHierarchy();

            Console.WriteLine("\tPass 3: Populating types...");

            AssemblyPopulator.EmitMetadataFiles = runtimeArgs.EnableMetadataGeneration;

            var methods = new List<(TypeDefinition type, List<CppMethodData> methods)>();

            //Create out dirs if needed
            var outputPath = Path.GetFullPath("cpp2il_out");
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            var methodOutputDir = Path.Combine(outputPath, "types");
            if ((runtimeArgs.EnableAnalysis || runtimeArgs.EnableMetadataGeneration) && !Directory.Exists(methodOutputDir))
                Directory.CreateDirectory(methodOutputDir);

            for (var imageIndex = 0; imageIndex < LibCpp2IlMain.TheMetadata.imageDefinitions.Length; imageIndex++)
            {
                var imageDef = LibCpp2IlMain.TheMetadata.imageDefinitions[imageIndex];

                Console.WriteLine($"\t\tPopulating {imageDef.typeCount} types in assembly {imageIndex + 1} of {LibCpp2IlMain.TheMetadata.imageDefinitions.Length}: {imageDef.Name}...");

                var assemblySpecificPath = Path.Combine(methodOutputDir, imageDef.Name.Replace(".dll", ""));
                if ((runtimeArgs.EnableMetadataGeneration || runtimeArgs.EnableAnalysis) && !Directory.Exists(assemblySpecificPath))
                    Directory.CreateDirectory(assemblySpecificPath);

                methods.AddRange(AssemblyPopulator.ProcessAssemblyTypes(LibCpp2IlMain.TheMetadata, LibCpp2IlMain.ThePe, imageDef));
            }

            //Invert dicts
            SharedState.ManagedToUnmanagedMethods = SharedState.UnmanagedToManagedMethods.ToDictionary(i => i.Value, i => i.Key);

            Console.WriteLine("\tPass 4: Applying type, method, and field attributes...");

            #region Attributes

            var unityEngineAssembly = Assemblies.Find(x => x.MainModule.Types.Any(t => t.Namespace == "UnityEngine" && t.Name == "SerializeField"));
            if (unityEngineAssembly != null)
            {
                foreach (var imageDef in LibCpp2IlMain.TheMetadata.imageDefinitions)
                {
                    //Cache these per-module.
                    var attributeCtorsByClassIndex = new Dictionary<long, MethodReference>();
                    
                    var lastTypeIndex = imageDef.firstTypeIndex + imageDef.typeCount;
                    for (var typeIndex = imageDef.firstTypeIndex; typeIndex < lastTypeIndex; typeIndex++)
                    {
                        var typeDef = LibCpp2IlMain.TheMetadata.typeDefs[typeIndex];
                        var typeDefinition = SharedState.UnmanagedToManagedTypes[typeDef!];

                        //Apply custom attributes to types
                        GetCustomAttributes(imageDef, typeDef.customAttributeIndex, typeDef.token, attributeCtorsByClassIndex, typeDefinition.Module)
                            .ForEach(attribute => typeDefinition.CustomAttributes.Add(attribute));

                        foreach (var fieldDef in typeDef.Fields!)
                        {
                            var fieldDefinition = SharedState.UnmanagedToManagedFields[fieldDef];

                            //Apply custom attributes to fields
                            GetCustomAttributes(imageDef, fieldDef.customAttributeIndex, fieldDef.token, attributeCtorsByClassIndex, typeDefinition.Module)
                                .ForEach(attribute => fieldDefinition.CustomAttributes.Add(attribute));
                        }
                        
                        foreach (var methodDef in typeDef.Methods!)
                        {
                            var methodDefinition = SharedState.UnmanagedToManagedMethods[methodDef];

                            //Apply custom attributes to methods
                            GetCustomAttributes(imageDef, methodDef.customAttributeIndex, methodDef.token, attributeCtorsByClassIndex, typeDefinition.Module)
                                .ForEach(attribute => methodDefinition.CustomAttributes.Add(attribute));
                        }
                    }
                }
            }

            #endregion

            KeyFunctionAddresses keyFunctionAddresses = null;
            if (runtimeArgs.EnableAnalysis)
            {
                Console.WriteLine("\tPass 5: Locating Globals...");

                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.TypeRefs.Count} type globals");
                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.MethodRefs.Count} method globals");
                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.FieldRefs.Count} field globals");
                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.Literals.Count} string literals");

                Console.WriteLine("\tPass 6: Looking for key functions...");

                //This part involves decompiling known functions to search for other function calls

                Disassembler.Translator.IncludeAddress = true;
                Disassembler.Translator.IncludeBinary = true;

                keyFunctionAddresses = KeyFunctionAddresses.Find(methods, LibCpp2IlMain.ThePe);
                
                Console.WriteLine("Pass 7: Finding Concrete Implementations of abstract classes...");
                
                foreach (var def in LibCpp2IlMain.TheMetadata.typeDefs)
                {
                    if(def.IsAbstract)
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

            #endregion

            Console.WriteLine("Saving Header DLLs to " + outputPath + "...");

            SaveHeaderDLLs(outputPath);

            if (runtimeArgs.EnableAnalysis)
            {

                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

                var methodTaintDict = DoAssemblyCSharpAnalysis(methodOutputDir, methods, keyFunctionAddresses!, out var total);

                if (total != 0)
                {

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
                }
            }

            Console.WriteLine("Done.");
            return 0;
        }

        private static List<CustomAttribute> GetCustomAttributes(Il2CppImageDefinition imageDef, int attributeIndex, uint token, IDictionary<long, MethodReference> attributeCtorsByClassIndex, ModuleDefinition module)
        {
            var attributes = new List<CustomAttribute>();

            //Get attributes and look for the serialize field attribute.
            var attributeTypeRange = LibCpp2IlMain.TheMetadata!.GetCustomAttributeIndex(imageDef, attributeIndex, token);

            if (attributeTypeRange == null) return attributes;

            //At AttributeGeneratorAddress there'll be a series of function calls, each one essentially taking the attribute type and its constructor params.
            //Float values are obtained using BitConverter.ToSingle(byte[], 0) with the 4 bytes making up the float.
            //FUTURE: Do we want to try to get the values for these?

            // var attributeGeneratorAddress = LibCpp2IlMain.ThePe!.customAttributeGenerators[Array.IndexOf(LibCpp2IlMain.TheMetadata.attributeTypeRanges, attributeTypeRange)];

            for (var attributeIdxIdx = 0; attributeIdxIdx < attributeTypeRange.count; attributeIdxIdx++)
            {
                var attributeTypeIndex = LibCpp2IlMain.TheMetadata.attributeTypes[attributeTypeRange.start + attributeIdxIdx];
                var attributeType = LibCpp2IlMain.ThePe!.types[attributeTypeIndex];
                if (attributeType.type != Il2CppTypeEnum.IL2CPP_TYPE_CLASS) continue;

                if (!attributeCtorsByClassIndex.ContainsKey(attributeType.data.classIndex))
                {
                    var cppAttribType = LibCpp2IlMain.TheMetadata.typeDefs[attributeType.data.classIndex];

                    // var attributeName = LibCpp2IlMain.TheMetadata.GetStringFromIndex(cppAttribType.nameIndex);
                    var cppMethodDefinition = cppAttribType.Methods!.First();
                    var managedCtor = SharedState.UnmanagedToManagedMethods[cppMethodDefinition];
                    attributeCtorsByClassIndex[attributeType.data.classIndex] = managedCtor;
                }

                // if (attributeName != "SerializeField") continue;
                var attributeConstructor = attributeCtorsByClassIndex[attributeType.data.classIndex];

                if (attributeConstructor.HasParameters)
                    continue; //Skip attributes which have arguments.
                
                var customAttribute = new CustomAttribute(module.ImportReference(attributeConstructor));
                attributes.Add(customAttribute);
            }

            return attributes;
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

        private static List<MethodReference> allCalledMethods = new List<MethodReference>();
        
        private static ConcurrentDictionary<string, AsmDumper.TaintReason> DoAssemblyCSharpAnalysis(string methodOutputDir, List<(TypeDefinition type, List<CppMethodData> methods)> methods, KeyFunctionAddresses keyFunctionAddresses, out int total)
        {
            var assembly = Assemblies.Find(a => a.Name.Name == "Assembly-CSharp" || a.Name.Name == "CSharp3" || a.Name.Name == "CSharp2");

            if (assembly == null)
            {
                total = 0;
                return new ConcurrentDictionary<string, AsmDumper.TaintReason>();
            }

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

                        var dumper = new AsmDumper(methodDefinition, method, methodStart, keyFunctionAddresses!, LibCpp2IlMain.ThePe);
                        var taintResult = dumper.AnalyzeMethod(typeDump, ref allUsedMnemonics);

                        foreach (var thisAction in dumper.Analysis.Actions)
                        {
                            if (!(thisAction is CallManagedFunctionAction callAction)) continue;

                            var m = callAction.ManagedMethodBeingCalled;

                            if (!allCalledMethods.Contains(m))
                                allCalledMethods.Add(m);
                        }


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
                catch (AnalysisExceptionRaisedException)
                {
                    //Ignore, logged already.
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

            var allMethods = toProcess.Select(s => s.type)
                .SelectMany(t => t.Methods)
                .Where(m => !m.IsVirtual)
                .ToList();
            
            var uncalledMethods = allMethods
                .Where(m => !allCalledMethods.Contains(m))
                .ToList();

            var output = $"There are {allMethods.Count} non-virtual methods in Assembly-CSharp, of which {uncalledMethods.Count} aren't called and {allCalledMethods.Count} are. Uncalled methods:\n";

            output += string.Join("\n", uncalledMethods);
            
            File.WriteAllText("cpp2il_out/uncalled_methods.txt", output);


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