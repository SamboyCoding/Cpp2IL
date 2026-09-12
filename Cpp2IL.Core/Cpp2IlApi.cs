using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using AssetRipper.Primitives;
using Cpp2IL.Core.Exceptions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;
using LibCpp2IL.Logging;

[assembly: InternalsVisibleTo("Cpp2IL.Core.Tests")]

namespace Cpp2IL.Core;

public static class Cpp2IlApi
{
    public static ApplicationAnalysisContext? CurrentAppContext;

    public static Cpp2IlRuntimeArgs? RuntimeOptions;

    internal static bool LowMemoryMode => RuntimeOptions?.LowMemoryMode ?? false;

    [RequiresUnreferencedCode("Plugins are loaded dynamically.")]
    public static void Init(string pluginsDir = "Plugins", bool loadExternalPlugins = true)
    {
        if (loadExternalPlugins)
            Cpp2IlPluginManager.LoadFromDirectory(ResolvePluginDirectory(pluginsDir));
        else
            Logger.InfoNewline("External plugin loading disabled.", "Plugins");

        Cpp2IlPluginManager.InitAll();
    }

    internal static string ResolvePluginDirectory(string pluginsDir)
    {
        if (string.IsNullOrWhiteSpace(pluginsDir))
            pluginsDir = "Plugins";

        return Path.GetFullPath(Path.IsPathRooted(pluginsDir)
            ? pluginsDir
            : Path.Combine(AppContext.BaseDirectory, pluginsDir));
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

        LibCpp2IlMain.Settings.MetadataFixupFunc = Cpp2IlPluginManager.MetadataFixupFuncs is { } funcs ? (originalBytes, version) =>
        {
            Logger.InfoNewline("Received request for metadata fixup from LibCpp2Il. Calling registered plugin fixup functions...");

            foreach (var func in funcs)
            {
                try
                {
                    var result = func(originalBytes, version);
                    if (result != null)
                    {
                        Logger.InfoNewline("Metadata fixup function returned non-null result, using this as fixed metadata.");
                        return result;
                    }
                }
                catch (Exception e)
                {
                    Logger.ErrorNewline($"Metadata fixup function threw an exception: {e}. Ignoring and trying next fixup function, if any...");
                }
            }

            //only get here if every fixup function returns null or throws.
            return null;
        } : null;

        LibLogger.Writer = new LibLogWriter();
    }

    [MemberNotNull(nameof(CurrentAppContext))]
    public static void InitializeLibCpp2Il(string assemblyPath, string metadataPath, UnityVersion unityVersion,
        bool allowUserToInputAddresses = false)
    {
        if (IsLibInitialized())
            ResetInternalState();

        ConfigureLib(allowUserToInputAddresses);

#if !DEBUG
        try
#endif
        {
            var context = LibCpp2IlMain.LoadFromFileAsContext(assemblyPath, metadataPath, unityVersion);
            OnLibInitialized(context);
        }
#if !DEBUG
        catch (Exception e)
        {
            throw new LibCpp2ILInitializationException("Fatal Exception initializing LibCpp2IL!", e);
        }
#endif

    }

    [MemberNotNull(nameof(CurrentAppContext))]
    public static void InitializeLibCpp2Il(byte[] assemblyData, byte[] metadataData, UnityVersion unityVersion,
        bool allowUserToInputAddresses = false)
    {
        if (IsLibInitialized())
            ResetInternalState();

        ConfigureLib(allowUserToInputAddresses);

        try
        {
            var context = LibCpp2IlMain.InitializeAsContext(assemblyData, metadataData, unityVersion);
            OnLibInitialized(context);
        }
        catch (Exception e)
        {
            throw new LibCpp2ILInitializationException("Fatal Exception initializing LibCpp2IL!", e);
        }
    }

    [MemberNotNull(nameof(CurrentAppContext))]
    private static void OnLibInitialized(LibCpp2IlContext libContext)
    {
        var start = DateTime.Now;
        Logger.InfoNewline("Creating application model...");
        CurrentAppContext = new(libContext);
        Logger.InfoNewline($"Application model created in {(DateTime.Now - start).TotalMilliseconds}ms");
    }

    public static void ResetInternalState()
    {
        CurrentAppContext = null;
    }

    private static bool IsLibInitialized()
    {
        return CurrentAppContext != null;
    }
}
