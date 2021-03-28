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

            try
            {
                var runtimeArgs = Cpp2IlTasks.GetRuntimeOptionsFromCommandLine(args);

                return MainWithArgs(runtimeArgs);
            }
            catch (SoftException e)
            {
                Console.WriteLine($"Execution Failed: {e.Message}");
                return -1;
            }
        }

        public static int MainWithArgs(Cpp2IlRuntimeArgs runtimeArgs)
        {
            if (!runtimeArgs.Valid)
                throw new SoftException("Arguments have Valid = false");
            
            Cpp2IlTasks.InitializeLibCpp2Il(runtimeArgs);
            
            Console.WriteLine("Building assemblies...This may take some time.");

            Assemblies = Cpp2IlTasks.MakeDummyDLLs(runtimeArgs);

            if (runtimeArgs.EnableMetadataGeneration) 
                Assemblies.ForEach(Cpp2IlTasks.GenerateMetadataForAssembly);

            Console.WriteLine("Applying type, method, and field attributes...This may take a couple of seconds");

            LibCpp2IlMain.TheMetadata!.imageDefinitions.ToList().ForEach(Cpp2IlTasks.ApplyCustomAttributesToAllTypesInAssembly);

            KeyFunctionAddresses keyFunctionAddresses = null;
            if (runtimeArgs.EnableAnalysis)
            {
                if (LibCpp2IlMain.Binary?.InstructionSet != InstructionSet.X86_32 && LibCpp2IlMain.Binary?.InstructionSet != InstructionSet.X86_64)
                    throw new NotImplementedException("Analysis engine is only implemented for x86. Use --skip-analysis to avoid this error.");
                
                Console.WriteLine("\tPass 5: Locating Globals...");

                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.TypeRefs.Count} type globals");
                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.MethodRefs.Count} method globals");
                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.FieldRefs.Count} field globals");
                Console.WriteLine($"\t\tFound {LibCpp2IlGlobalMapper.Literals.Count} string literals");

                Console.WriteLine("\tPass 6: Looking for key functions...");

                //This part involves decompiling known functions to search for other function calls

                Disassembler.Translator.IncludeAddress = true;
                Disassembler.Translator.IncludeBinary = true;

                keyFunctionAddresses = KeyFunctionAddresses.Find();
                
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

            var outputPath = Path.GetFullPath("cpp2il_out");
            var methodOutputDir = Path.Combine(outputPath, "types");
            
            Console.WriteLine("Saving Header DLLs to " + outputPath + "...");
            Cpp2IlTasks.SaveAssemblies(outputPath, Assemblies);

            if (runtimeArgs.EnableAnalysis)
            {
                var methodTaintDict = DoAssemblyCSharpAnalysis(methodOutputDir, keyFunctionAddresses!, out var total);

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

        private static List<MethodReference> allCalledMethods = new List<MethodReference>();
        
        private static ConcurrentDictionary<string, AsmDumper.TaintReason> DoAssemblyCSharpAnalysis(string methodOutputDir, KeyFunctionAddresses keyFunctionAddresses, out int total)
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
            var toProcess = assembly.MainModule.Types.Where(t => t.Namespace != AssemblyPopulator.InjectedNamespaceName).ToList();
            //Sort alphabetically by type.
            toProcess.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
            var thresholds = new[] {10, 20, 30, 40, 50, 60, 70, 80, 90, 100}.ToList();
            var nextThreshold = thresholds.First();

            var successfullyProcessed = 0;
            var failedProcess = 0;

            var startTime = DateTime.Now;

            var methodTaintDict = new ConcurrentDictionary<string, AsmDumper.TaintReason>();

            thresholds.RemoveAt(0);


            Action<TypeDefinition> action = type =>
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

                    foreach (var methodDefinition in type.Methods)
                    {
                        var methodStart = methodDefinition.AsUnmanaged().MethodPointer;

                        if (methodStart == 0) continue;

                        var dumper = new AsmDumper(methodDefinition, methodStart, keyFunctionAddresses!);
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

            var allMethods = toProcess
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