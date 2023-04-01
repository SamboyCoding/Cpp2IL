using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        public static ApplicationAnalysisContext? CurrentAppContext;

        public static void Init(string pluginsDir = "Plugins")
        {
            Cpp2IlPluginManager.LoadFromDirectory(Path.Combine(Environment.CurrentDirectory, pluginsDir));
            Cpp2IlPluginManager.InitAll();
        }

        public static UnityVersion DetermineUnityVersion(string? unityPlayerPath, string? gameDataPath)
            => LibCpp2IlMain.DetermineUnityVersion(unityPlayerPath, gameDataPath);

        public static UnityVersion GetVersionFromGlobalGameManagers(byte[] ggmBytes)
            => LibCpp2IlMain.GetVersionFromGlobalGameManagers(ggmBytes);

        public static UnityVersion GetVersionFromDataUnity3D(Stream fileStream)
            => LibCpp2IlMain.GetVersionFromDataUnity3D(fileStream);

        public static void ConfigureLib(bool allowUserToInputAddresses)
        {
            //Set this flag from the options
            LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput = allowUserToInputAddresses;

            //We have to have this on, despite the cost, because we need them for attribute restoration
            LibCpp2IlMain.Settings.DisableMethodPointerMapping = false;

            LibLogger.Writer = new LibLogWriter();
        }

        [MemberNotNull(nameof(CurrentAppContext))]
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

        [MemberNotNull(nameof(CurrentAppContext))]
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

        [MemberNotNull(nameof(CurrentAppContext))]
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
