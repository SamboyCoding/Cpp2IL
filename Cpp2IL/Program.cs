using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using LibCpp2IL;
using Mono.Cecil;
using SharpDisasm;

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

            Assemblies = Cpp2IlTasks.MakeDummyDLLs(runtimeArgs);

            if (runtimeArgs.EnableMetadataGeneration)
                Assemblies.ForEach(Cpp2IlTasks.GenerateMetadataForAssembly);

            KeyFunctionAddresses? keyFunctionAddresses = null;

            //We have to always run key function scan (if we can), so that attribute reconstruction can run.
            if (LibCpp2IlMain.Binary?.InstructionSet == InstructionSet.X86_32 || LibCpp2IlMain.Binary?.InstructionSet == InstructionSet.X86_64)
            {
                Console.WriteLine("Running Scan for Known Functions...");

                Disassembler.Translator.IncludeAddress = true;
                Disassembler.Translator.IncludeBinary = true;

                //This part involves decompiling known functions to search for other function calls
                keyFunctionAddresses = KeyFunctionAddresses.Find();
            }

            Console.WriteLine("Applying type, method, and field attributes...This may take a couple of seconds");
            var start = DateTime.Now;

            LibCpp2IlMain.TheMetadata!.imageDefinitions.ToList().ForEach(definition => AttributeRestorer.ApplyCustomAttributesToAllTypesInAssembly(definition, keyFunctionAddresses));

            Console.WriteLine($"Finished Applying Attributes in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

            if (runtimeArgs.EnableAnalysis)
            {
                if (LibCpp2IlMain.Binary?.InstructionSet != InstructionSet.X86_32 && LibCpp2IlMain.Binary?.InstructionSet != InstructionSet.X86_64)
                    throw new NotImplementedException("Analysis engine is only implemented for x86. Use --skip-analysis to avoid this error.");

                Console.WriteLine("Populating Concrete Implementation Table...");

                foreach (var def in LibCpp2IlMain.TheMetadata.typeDefs)
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

            var outputPath = Path.GetFullPath("cpp2il_out");
            var methodOutputDir = Path.Combine(outputPath, "types");

            Console.WriteLine("Saving Header DLLs to " + outputPath + "...");
            Cpp2IlTasks.SaveAssemblies(outputPath, Assemblies);

            if (runtimeArgs.EnableAnalysis)
            {
                DoAssemblyCSharpAnalysis(methodOutputDir, keyFunctionAddresses!);
            }

            Console.WriteLine("Done.");
            return 0;
        }

        private static void DoAssemblyCSharpAnalysis(string methodOutputDir, KeyFunctionAddresses keyFunctionAddresses)
        {
            var assemblyCsharp = Assemblies.Find(a => a.Name.Name == "Assembly-CSharp" || a.Name.Name == "CSharp3" || a.Name.Name == "CSharp2");

            if (assemblyCsharp == null)
            {
                return;
            }

            Cpp2IlTasks.AnalyseAssembly(assemblyCsharp, keyFunctionAddresses, methodOutputDir, false);
        }
    }
}