using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Exceptions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.Logging;

namespace Cpp2IL.Core
{
    public static class Cpp2IlApi
    {
        private static Regex unityVersionRegex = new Regex(@"^[0-9]+\.[0-9]+\.[0-9]+[abcfx][0-9]+$", RegexOptions.Compiled);
        public static ApplicationAnalysisContext? CurrentAppContext;

        public static void Init(string pluginsDir = "Plugins")
        {
            Cpp2IlPluginManager.LoadFromDirectory(Path.Combine(Environment.CurrentDirectory, pluginsDir));
            Cpp2IlPluginManager.InitAll();
        }

        public static UnityVersion DetermineUnityVersion(string unityPlayerPath, string gameDataPath)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && !string.IsNullOrEmpty(unityPlayerPath))
            {
                var unityVer = FileVersionInfo.GetVersionInfo(unityPlayerPath);

                return new UnityVersion((ushort)unityVer.FileMajorPart, (ushort)unityVer.FileMinorPart, (ushort)unityVer.FileBuildPart);
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

            return default;
        }

        public static UnityVersion GetVersionFromGlobalGameManagers(byte[] ggmBytes)
        {
            var verString = new StringBuilder();
            var idx = 0x14;
            while (ggmBytes[idx] != 0)
            {
                verString.Append(Convert.ToChar(ggmBytes[idx]));
                idx++;
            }

            string unityVer = verString.ToString();

            if (!unityVersionRegex.IsMatch(unityVer))
            {
                idx = 0x30;
                verString = new StringBuilder();
                while (ggmBytes[idx] != 0)
                {
                    verString.Append(Convert.ToChar(ggmBytes[idx]));
                    idx++;
                }

                unityVer = verString.ToString().Trim();
            }

            return UnityVersion.Parse(unityVer);
        }

        public static UnityVersion GetVersionFromDataUnity3D(Stream fileStream)
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

            var unityVer = verString.ToString().Trim();

            return UnityVersion.Parse(unityVer);
        }

        private static void ConfigureLib(bool allowUserToInputAddresses)
        {
            //Set this flag from the options
            LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput = allowUserToInputAddresses;

            //We have to have this on, despite the cost, because we need them for attribute restoration
            LibCpp2IlMain.Settings.DisableMethodPointerMapping = false;

            LibLogger.Writer = new LibLogWriter();
        }

        public static void InitializeLibCpp2Il(string assemblyPath, string metadataPath, UnityVersion unityVersion, bool allowUserToInputAddresses = false)
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

        public static void InitializeLibCpp2Il(byte[] assemblyData, byte[] metadataData, UnityVersion unityVersion, bool allowUserToInputAddresses = false)
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

            var start = DateTime.Now;
            Logger.InfoNewline("Creating application model...");
            CurrentAppContext = new(LibCpp2IlMain.Binary, LibCpp2IlMain.TheMetadata!, LibCpp2IlMain.MetadataVersion);
            Logger.InfoNewline($"Application model created in {(DateTime.Now - start).TotalMilliseconds}ms");
        }

        private static void ResetInternalState()
        {
            SharedState.Clear();

            MiscUtils.Reset();

            LibCpp2IlMain.Reset();
        }
        
        // public static void PopulateConcreteImplementations()
        // {
        //     CheckLibInitialized();
        //
        //     Logger.InfoNewline("Populating Concrete Implementation Table...");
        //
        //     foreach (var def in LibCpp2IlMain.TheMetadata!.typeDefs)
        //     {
        //         if (def.IsAbstract)
        //             continue;
        //
        //         var baseTypeReflectionData = def.BaseType;
        //         while (baseTypeReflectionData != null)
        //         {
        //             if (baseTypeReflectionData.baseType == null)
        //                 break;
        //
        //             if (baseTypeReflectionData.isType && baseTypeReflectionData.baseType.IsAbstract && !SharedState.ConcreteImplementations.ContainsKey(baseTypeReflectionData.baseType))
        //                 SharedState.ConcreteImplementations[baseTypeReflectionData.baseType] = def;
        //
        //             baseTypeReflectionData = baseTypeReflectionData.baseType.BaseType;
        //         }
        //     }
        // }

        private static bool IsLibInitialized()
        {
            return LibCpp2IlMain.Binary != null && LibCpp2IlMain.TheMetadata != null;
        }
    }
}