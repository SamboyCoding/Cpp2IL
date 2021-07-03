using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Cpp2IL.Analysis;
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
            Console.WriteLine("A Tool to Reverse Unity's \"il2cpp\" Build Process.\n");
            
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Logger.DisableColor = true;
                Logger.WarnNewline("Looks like you're running on a non-windows platform. Disabling ANSI colour codes.");
            } else if (Directory.Exists(@"Z:\usr\"))
            {
                Logger.DisableColor = true;
                Logger.WarnNewline("Looks like you're running in wine or proton. Disabling ANSI colour codes.");
            }
            
            Logger.InfoNewline("Running on " + Environment.OSVersion.Platform);

            try
            {
                var runtimeArgs = Cpp2IlTasks.GetRuntimeOptionsFromCommandLine(args);

                return MainWithArgs(runtimeArgs);
            }
            catch (SoftException e)
            {
                Logger.ErrorNewline($"Execution Failed: {e.Message}");
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
                Logger.InfoNewline("Running Scan for Known Functions...");

                Disassembler.Translator.IncludeAddress = true;
                Disassembler.Translator.IncludeBinary = true;

                //This part involves decompiling known functions to search for other function calls
                keyFunctionAddresses = KeyFunctionAddresses.Find();
            }

            Logger.InfoNewline("Applying type, method, and field attributes...This may take a couple of seconds");
            var start = DateTime.Now;

            LibCpp2IlMain.TheMetadata!.imageDefinitions.ToList().ForEach(definition => AttributeRestorer.ApplyCustomAttributesToAllTypesInAssembly(definition, keyFunctionAddresses));

            Logger.InfoNewline($"Finished Applying Attributes in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

            if (runtimeArgs.EnableAnalysis)
            {
                if (LibCpp2IlMain.Binary?.InstructionSet != InstructionSet.X86_32 && LibCpp2IlMain.Binary?.InstructionSet != InstructionSet.X86_64)
                    throw new NotImplementedException("Analysis engine is only implemented for x86. Use --skip-analysis to avoid this error.");

                Logger.InfoNewline("Populating Concrete Implementation Table...");

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

            Logger.InfoNewline("Saving Header DLLs to " + outputPath + "...");
            Cpp2IlTasks.SaveAssemblies(outputPath, Assemblies);

            if (runtimeArgs.EnableAnalysis)
            {
                DoAssemblyCSharpAnalysis(runtimeArgs, methodOutputDir, keyFunctionAddresses!);
            }

            Logger.InfoNewline("Done.");
            return 0;
        }

        private static void DoAssemblyCSharpAnalysis(Cpp2IlRuntimeArgs args, string methodOutputDir, KeyFunctionAddresses keyFunctionAddresses)
        {
            var assemblyCsharp = Assemblies.Find(a => a.Name.Name == "Assembly-CSharp" || a.Name.Name == "CSharp3" || a.Name.Name == "CSharp2");

            if (assemblyCsharp == null)
            {
                return;
            }

            Cpp2IlTasks.AnalyseAssembly(args, assemblyCsharp, keyFunctionAddresses, methodOutputDir, false);
        }
    }
}