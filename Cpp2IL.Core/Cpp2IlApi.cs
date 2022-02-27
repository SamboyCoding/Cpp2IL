using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Exceptions;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.Logging;

namespace Cpp2IL.Core
{
    public static class Cpp2IlApi
    {
        public static bool IlContinueThroughErrors;
        public static ApplicationAnalysisContext? CurrentAppContext;

        private static readonly HashSet<string> ForbiddenDirectoryNames = new()
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9"
        };

        public static void Init()
        {
            Cpp2IlPluginManager.InitAll();
        }

        public static int[]? DetermineUnityVersion(string unityPlayerPath, string gameDataPath)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && !string.IsNullOrEmpty(unityPlayerPath))
            {
                var unityVer = FileVersionInfo.GetVersionInfo(unityPlayerPath);

                return new[] {unityVer.FileMajorPart, unityVer.FileMinorPart, unityVer.FileBuildPart};
            }

            if (!string.IsNullOrEmpty(gameDataPath))
            {
                //Globalgamemanagers
                var globalgamemanagersPath = Path.Combine(gameDataPath, "globalgamemanagers");
                if (File.Exists(globalgamemanagersPath))
                {
                    var ggmBytes = File.ReadAllBytes(globalgamemanagersPath);
                    return GetVersionFromGlobalGameManagers(ggmBytes);
                }

                //Data.unity3d
                var dataPath = Path.Combine(gameDataPath, "data.unity3d");
                if (File.Exists(dataPath))
                {
                    using var dataStream = File.OpenRead(dataPath);
                    return GetVersionFromDataUnity3D(dataStream);
                }
            }

            return null;
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

            if (fileStream.CanSeek)
                fileStream.Seek(0x12, SeekOrigin.Begin);
            else
                fileStream.Read(new byte[0x12], 0, 0x12);

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

        public static void InitializeLibCpp2Il(string assemblyPath, string metadataPath, int[] unityVersion, bool allowUserToInputAddresses = false)
        {
            if (IsLibInitialized())
                ResetInternalState();

            ConfigureLib(allowUserToInputAddresses);

#if !DEBUG
            try
            {
#endif
            if (!LibCpp2IlMain.LoadFromFile(assemblyPath, metadataPath, unityVersion))
                throw new Exception("Initialization with LibCpp2Il failed");
#if !DEBUG
            }
            catch (Exception e)
            {
                throw new LibCpp2ILInitializationException("Fatal Exception initializing LibCpp2IL!", e);
            }
#endif
            OnLibInitialized();
        }

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
            
            OnLibInitialized();
        }

        private static void OnLibInitialized()
        {
            MiscUtils.Init();
            LibCpp2IlMain.Binary!.AllCustomAttributeGenerators.ToList().ForEach(ptr => SharedState.AttributeGeneratorStarts.Add(ptr));
            
            Logger.Info("Creating application model...");
            CurrentAppContext = new(LibCpp2IlMain.Binary, LibCpp2IlMain.TheMetadata!, LibCpp2IlMain.MetadataVersion);
            Logger.InfoNewline("Done.");
        }

        private static void ResetInternalState()
        {
            SharedState.Clear();

            MiscUtils.Reset();

            LibCpp2IlMain.Reset();
        }

        public static void PopulateCustomAttributesForAssembly(AssemblyAnalysisContext assembly)
        {
            assembly.AnalyzeCustomAttributeData();

            foreach (var typeAnalysisContext in assembly.Types)
            {
                typeAnalysisContext.AnalyzeCustomAttributeData();
                
                typeAnalysisContext.Methods.ForEach(m => m.AnalyzeCustomAttributeData());
                typeAnalysisContext.Fields.ForEach(f => f.AnalyzeCustomAttributeData());
                typeAnalysisContext.Properties.ForEach(p => p.AnalyzeCustomAttributeData());
                typeAnalysisContext.Events.ForEach(e => e.AnalyzeCustomAttributeData());
            }
        }

        public static void GenerateMetadataForAllAssemblies(string rootFolder)
        {
            CheckLibInitialized();

            //TODO decide if we want to reimplement or discard in favour of something else.
            // foreach (var assemblyDefinition in SharedState.AssemblyList)
            //     GenerateMetadataForAssembly(rootFolder, assemblyDefinition);
        }

        // public static void GenerateMetadataForAssembly(string rootFolder, AssemblyDefinition assemblyDefinition)
        // {
        //     // foreach (var mainModuleType in assemblyDefinition.MainModule.Types.Where(mainModuleType => mainModuleType.Namespace != AssemblyPopulator.InjectedNamespaceName))
        //     // {
        //     //     GenerateMetadataForType(rootFolder, mainModuleType);
        //     // }
        // }

        // public static void GenerateMetadataForType(string rootFolder, TypeDefinition typeDefinition)
        // {
        //     CheckLibInitialized();
        //
        //     var assemblyPath = Path.Combine(rootFolder, "types", typeDefinition.Module.Assembly.Name.Name);
        //     if (!Directory.Exists(assemblyPath))
        //         Directory.CreateDirectory(assemblyPath);
        //
        //     File.WriteAllText(
        //         Path.Combine(assemblyPath, typeDefinition.Name.Replace("<", "_").Replace(">", "_").Replace("|", "_") + "_metadata.txt"),
        //         AssemblyPopulator.BuildWholeMetadataString(typeDefinition)
        //     );
        // }

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