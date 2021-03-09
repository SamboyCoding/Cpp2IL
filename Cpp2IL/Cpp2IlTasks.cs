using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using CommandLine;
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
            LibCpp2IlMain.Settings.DisableMethodPointerMapping = LibCpp2IlMain.Settings.DisableGlobalResolving = runtimeArgs.EnableAnalysis;

            if (!LibCpp2IlMain.LoadFromFile(runtimeArgs.PathToAssembly, runtimeArgs.PathToMetadata, runtimeArgs.UnityVersion))
                throw new SoftException("Initialization with LibCpp2Il failed");
        }

        public static List<AssemblyDefinition> MakeDummyDLLs(Cpp2IlRuntimeArgs runtimeArgs)
        {
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

            foreach (var imageDef in LibCpp2IlMain.TheMetadata.imageDefinitions)
            {
                var assemblySpecificPath = Path.Combine(methodOutputDir, imageDef.Name!.Replace(".dll", ""));
                if (runtimeArgs.EnableMetadataGeneration && !Directory.Exists(assemblySpecificPath))
                    Directory.CreateDirectory(assemblySpecificPath);

                AssemblyPopulator.PopulateStubTypesInAssembly(imageDef);
            }

            return Assemblies;
        }

        public static void GenerateMetadataForAssembly(AssemblyDefinition assemblyDefinition)
        {
            foreach (var mainModuleType in assemblyDefinition.MainModule.Types)
            {
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

        public static void SaveAssemblies(string containingFolder, List<AssemblyDefinition> assemblies)
        {
            foreach (var assembly in assemblies)
            {
                var dllPath = Path.Combine(containingFolder, assembly.MainModule.Name);

                //Remove NetCore Dependencies 
                var reference = assembly.MainModule.AssemblyReferences.FirstOrDefault(a => a.Name == "System.Private.CoreLib");
                if (reference != null)
                    assembly.MainModule.AssemblyReferences.Remove(reference);

                assembly.Write(dllPath);
            }
        }
    }
}