using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CommandLine;
using Cpp2IL.Analysis;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using LibCpp2IL.PE;
using Mono.Cecil;

namespace Cpp2IL
{
    public class Cpp2IlTasks
    {
        private static readonly string[] BlacklistedExecutableFilenames =
        {
            "UnityCrashHandler.exe",
            "UnityCrashHandler64.exe",
            "install.exe",
            "MelonLoader.Installer.exe"
        };

        public static Cpp2IlRuntimeArgs GetRuntimeOptionsFromCommandLine(string[] commandLine)
        {
            var parserResult = Parser.Default.ParseArguments<CommandLineArgs>(commandLine);

            if (!(parserResult is Parsed<CommandLineArgs> {Value: { } options}))
                throw new SoftException("Failed to parse command line arguments");

            if (!options.AreForceOptionsValid)
                throw new SoftException("Invalid force option configuration");

            var result = new Cpp2IlRuntimeArgs();

            if (options.ForcedBinaryPath == null)
            {
                var baseGamePath = options.GamePath;

                if (!Directory.Exists(baseGamePath))
                    throw new SoftException($"Specified game-path does not exist: {baseGamePath}");

                result.PathToAssembly = Path.Combine(baseGamePath, "GameAssembly.dll");
                var exeName = Path.GetFileNameWithoutExtension(Directory.GetFiles(baseGamePath)
                    .First(f => f.EndsWith(".exe") && !BlacklistedExecutableFilenames.Any(bl => f.EndsWith(bl))));

                exeName = options.ExeName ?? exeName;

                var unityPlayerPath = Path.Combine(baseGamePath, $"{exeName}.exe");
                result.PathToMetadata = Path.Combine(baseGamePath, $"{exeName}_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

                if (!File.Exists(result.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(result.PathToMetadata))
                    throw new SoftException("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
                                            $"\t{result.PathToAssembly}\n" +
                                            $"\t{unityPlayerPath}\n" +
                                            $"\t{result.PathToMetadata}\n");

                result.UnityVersion = DetermineUnityVersion(unityPlayerPath, Path.Combine(baseGamePath, $"{exeName}_Data"));

                if (result.UnityVersion[0] <= 4)
                    throw new SoftException($"Unable to determine a valid unity version (got {result.UnityVersion.ToStringEnumerable()})");

                result.Valid = true;
            }
            else
            {
                Console.WriteLine("Warning: Using force options, I sure hope you know what you're doing!");
                result.PathToAssembly = options.ForcedBinaryPath!;
                result.PathToMetadata = options.ForcedMetadataPath!;
                result.UnityVersion = options.ForcedUnityVersion!.Split('.').Select(int.Parse).ToArray();
                result.Valid = true;
            }

            result.EnableAnalysis = !options.SkipAnalysis;
            result.EnableMetadataGeneration = !options.SkipMetadataTextFiles;
            result.EnableRegistrationPrompts = !options.DisableRegistrationPrompts;

            return result;
        }

        private static int[] DetermineUnityVersion(string unityPlayerPath, string gameDataPath)
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
                var verString = new StringBuilder();
                var idx = 0x14;
                while (ggmBytes[idx] != 0)
                {
                    verString.Append(Convert.ToChar(ggmBytes[idx]));
                    idx++;
                }

                var unityVer = verString.ToString();
                unityVer = unityVer.Substring(0, unityVer.IndexOf("f", StringComparison.Ordinal));
                version = unityVer.Split(".").Select(int.Parse).ToArray();
            }

            return version;
        }

        public static void InitializeLibCpp2Il(Cpp2IlRuntimeArgs runtimeArgs)
        {
            //Set this flag from command line options
            LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput = runtimeArgs.EnableRegistrationPrompts;

            //Disable Method Ptr Mapping and Global Resolving if skipping analysis
            LibCpp2IlMain.Settings.DisableMethodPointerMapping = LibCpp2IlMain.Settings.DisableGlobalResolving = !runtimeArgs.EnableAnalysis;

            try
            {
                if (!LibCpp2IlMain.LoadFromFile(runtimeArgs.PathToAssembly, runtimeArgs.PathToMetadata, runtimeArgs.UnityVersion))
                    throw new SoftException("Initialization with LibCpp2Il failed");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"\n\nFatal Exception initializing LibCpp2IL!\n{e}\n\nWaiting for you to press enter - feel free to copy the error...");
                Console.ReadLine();
                Environment.Exit(-1);
            }
        }

        public static List<AssemblyDefinition> MakeDummyDLLs(Cpp2IlRuntimeArgs runtimeArgs)
        {
            Console.WriteLine("Building assemblies...This may take some time.");
            var start = DateTime.Now;
            
            var resolver = new RegistryAssemblyResolver();
            var moduleParams = new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                AssemblyResolver = resolver,
                MetadataResolver = new MetadataResolver(resolver)
            };

            //Make stub types
            var Assemblies = StubAssemblyBuilder.BuildStubAssemblies(LibCpp2IlMain.TheMetadata!, moduleParams);
            Assemblies.ForEach(resolver.Register);

            //Configure utils class
            Utils.BuildPrimitiveMappings();

            //Set base types and interfaces
            AssemblyPopulator.ConfigureHierarchy();

            //Create out dirs if needed
            var outputPath = Path.GetFullPath("cpp2il_out");
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            var methodOutputDir = Path.Combine(outputPath, "types");
            if ((runtimeArgs.EnableAnalysis || runtimeArgs.EnableMetadataGeneration) && !Directory.Exists(methodOutputDir))
                Directory.CreateDirectory(methodOutputDir);

            foreach (var imageDef in LibCpp2IlMain.TheMetadata!.imageDefinitions)
            {
                var assemblySpecificPath = Path.Combine(methodOutputDir, imageDef.Name!.Replace(".dll", ""));
                if (runtimeArgs.EnableMetadataGeneration && !Directory.Exists(assemblySpecificPath))
                    Directory.CreateDirectory(assemblySpecificPath);

                AssemblyPopulator.PopulateStubTypesInAssembly(imageDef);
            }
            
            Console.WriteLine($"Finished Building Assemblies in {(DateTime.Now - start).TotalMilliseconds:F0}ms");
            Console.WriteLine("Fixing up explicit overrides. Any warnings you see here aren't errors - they usually indicate improperly stripped or obfuscated types, but this is not a big deal. This should only take a second...");
            start = DateTime.Now;
            
            //Fixup explicit overrides.
            foreach (var imageDef in LibCpp2IlMain.TheMetadata.imageDefinitions) 
                AssemblyPopulator.FixupExplicitOverridesInAssembly(imageDef);
            
            Console.WriteLine($"Fixup complete ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");

            return Assemblies;
        }

        public static void GenerateMetadataForAssembly(AssemblyDefinition assemblyDefinition)
        {
            foreach (var mainModuleType in assemblyDefinition.MainModule.Types)
            {
                if(mainModuleType.Namespace == AssemblyPopulator.InjectedNamespaceName)
                    continue;
                
                GenerateMetadataForType(mainModuleType);
            }
        }

        public static void GenerateMetadataForType(TypeDefinition typeDefinition)
        {
            File.WriteAllText(
                Path.Combine(Path.GetFullPath("cpp2il_out"), "types", typeDefinition.Module.Assembly.Name.Name, typeDefinition.Name.Replace("<", "_").Replace(">", "_").Replace("|", "_") + "_metadata.txt"),
                AssemblyPopulator.BuildWholeMetadataString(typeDefinition));
        }

        public static void ApplyCustomAttributesToAllTypesInAssembly(Il2CppImageDefinition imageDef)
        {
            //Cache these per-module.
            var attributeCtorsByClassIndex = new Dictionary<long, MethodReference>();

            foreach (var typeDef in imageDef.Types!)
            {
                var typeDefinition = SharedState.UnmanagedToManagedTypes[typeDef];

                //Apply custom attributes to types
                GetCustomAttributesByAttributeIndex(imageDef, typeDef.customAttributeIndex, typeDef.token, attributeCtorsByClassIndex, typeDefinition.Module)
                    .ForEach(attribute => typeDefinition.CustomAttributes.Add(attribute));

                foreach (var fieldDef in typeDef.Fields!)
                {
                    var fieldDefinition = SharedState.UnmanagedToManagedFields[fieldDef];

                    //Apply custom attributes to fields
                    GetCustomAttributesByAttributeIndex(imageDef, fieldDef.customAttributeIndex, fieldDef.token, attributeCtorsByClassIndex, typeDefinition.Module)
                        .ForEach(attribute => fieldDefinition.CustomAttributes.Add(attribute));
                }

                foreach (var methodDef in typeDef.Methods!)
                {
                    var methodDefinition = SharedState.UnmanagedToManagedMethods[methodDef];

                    //Apply custom attributes to methods
                    GetCustomAttributesByAttributeIndex(imageDef, methodDef.customAttributeIndex, methodDef.token, attributeCtorsByClassIndex, typeDefinition.Module)
                        .ForEach(attribute => methodDefinition.CustomAttributes.Add(attribute));
                }
            }
        }

        public static List<CustomAttribute> GetCustomAttributesByAttributeIndex(Il2CppImageDefinition imageDef, int attributeIndex, uint token, IDictionary<long, MethodReference> attributeCtorsByClassIndex, ModuleDefinition module)
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
                var attributeType = LibCpp2IlMain.Binary!.GetType(attributeTypeIndex);
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

        public static void SaveAssemblies(string containingFolder, List<AssemblyDefinition> assemblies)
        {
            foreach (var assembly in assemblies)
            {
                var dllPath = Path.Combine(containingFolder, assembly.MainModule.Name);

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
                    Console.Error.WriteLine($"\n\nFatal Exception writing DLL {dllPath}!\n{e}\n\nWaiting for you to press enter - feel free to copy the error...");
                    Console.ReadLine();
                    Environment.Exit(-1);
                }
            }
        }

        public static void AnalyseAssembly(AssemblyDefinition assembly, KeyFunctionAddresses keyFunctionAddresses, string methodOutputDir, bool parallel)
        {
            Console.WriteLine("Dumping method bytes to " + methodOutputDir);
            Directory.CreateDirectory(Path.Combine(methodOutputDir, assembly.Name.Name));

            var counter = 0;
            var toProcess = assembly.MainModule.Types.Where(t => t.Namespace != AssemblyPopulator.InjectedNamespaceName).ToList();
            //Sort alphabetically by type.
            toProcess.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
            var thresholds = new[] {10, 20, 30, 40, 50, 60, 70, 80, 90, 100}.ToList();
            var nextThreshold = thresholds.First();

            var successfullyProcessed = 0;

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
                            Console.WriteLine($"{nextThreshold}% ({counter} classes in {Math.Round(elapsedSoFar.TotalSeconds)} sec, ~{Math.Round(rate)} classes / sec, {remaining} classes remaining, approx {Math.Round(remaining / rate + 5)} sec remaining)");
                            nextThreshold = thresholds.First();
                            thresholds.RemoveAt(0);
                        }
                    }
                }

                try
                {
                    var filename = Path.Combine(methodOutputDir, assembly.Name.Name, type.Name.Replace("<", "_").Replace(">", "_").Replace("|", "_") + "_methods.txt");
                    var typeDump = new StringBuilder("Type: " + type.Name + "\n\n");

                    foreach (var methodDefinition in type.Methods)
                    {
                        var methodStart = methodDefinition.AsUnmanaged().MethodPointer;

                        if (methodStart == 0) continue;

                        var dumper = new AsmDumper(methodDefinition, methodStart, keyFunctionAddresses!);
                        dumper.AnalyzeMethod(typeDump);

                        Interlocked.Increment(ref successfullyProcessed);
                    }

                    lock (type) File.WriteAllText(filename, typeDump.ToString());
                }
                catch (AnalysisExceptionRaisedException)
                {
                    //Ignore, logged already.
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to dump methods for type " + type.Name + " " + e);
                }
            }

            if (parallel)
                toProcess.AsParallel().ForAll(ProcessType);
            else
                toProcess.ForEach(ProcessType);
            
            var elapsed = DateTime.Now - startTime;
            Console.WriteLine($"Finished processing {successfullyProcessed} methods in {elapsed.Ticks} ticks (about {Math.Round(elapsed.TotalSeconds, 1)} seconds), at an overall rate of about {Math.Round(toProcess.Count / elapsed.TotalSeconds)} methods/sec");
        }
    }
}