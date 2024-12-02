using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime;
using CommandLine;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
#if !DEBUG
using Cpp2IL.Core.Exceptions;
#endif
using LibCpp2IL.Wasm;
using AssetRipper.Primitives;
using Cpp2IL.Core.Extensions;
using LibCpp2IL;

namespace Cpp2IL;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal class Program
{
    private static readonly List<string> PathsToDeleteOnExit = [];

    public static readonly string Cpp2IlVersionString = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    private static void ResolvePathsFromCommandLine(string gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
    {
        if (string.IsNullOrEmpty(gamePath))
            throw new SoftException("No force options provided, and no game path was provided either. Please provide a game path or use the --force- options.");

        Logger.VerboseNewline("Beginning path resolution...");

        if (Directory.Exists(gamePath) && File.Exists(Path.Combine(gamePath, "Contents/Frameworks/GameAssembly.dylib")))
            HandleMacOSGamePath(gamePath, inputExeName, ref args);
        else if (Directory.Exists(gamePath) && File.Exists(Path.Combine(gamePath, "GameAssembly.so")))
            HandleLinuxGamePath(gamePath, inputExeName, ref args);
        else if (Directory.Exists(gamePath))
            HandleWindowsGamePath(gamePath, inputExeName, ref args);
        else if (File.Exists(gamePath) && Path.GetExtension(gamePath).ToLowerInvariant() == ".apk")
            HandleSingleApk(gamePath, ref args);
        else if (File.Exists(gamePath) && Path.GetExtension(gamePath).ToLowerInvariant() is ".xapk" or ".apkm")
            HandleXapk(gamePath, ref args);
        else if (File.Exists(gamePath) && Path.GetExtension(gamePath).ToLowerInvariant() is ".ipa" or ".tipa")
            HandleIpa(gamePath, ref args);
        else
        {
            if (!Cpp2IlPluginManager.TryProcessGamePath(gamePath, ref args))
                throw new SoftException($"Could not find a valid unity game at {gamePath}");
        }
    }

    private static void HandleMacOSGamePath(string gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
    {
        //macOS game.
        args.PathToAssembly = Path.Combine(gamePath, "Contents", "Frameworks", "GameAssembly.dylib");
        var exeName = Path.GetFileName(Directory.GetFiles(Path.Combine(gamePath, "Contents", "MacOS"))
            .FirstOrDefault(f => MiscUtils.BlacklistedExecutableFilenames.Any(f.EndsWith)));

        exeName = inputExeName ?? exeName;

        Logger.VerboseNewline($"Trying HandleMacOSGamePath as provided bundle contains GameAssembly.dylib, potential GA is {args.PathToAssembly} and executable {exeName}");

        if (exeName == null)
            throw new SoftException("Failed to locate any executable in the provided game directory. Make sure the path is correct, and if you *really* know what you're doing (and know it's not supported), use the force options, documented if you provide --help.");

        var unityPlayerPath = Path.Combine(gamePath, "Contents", "MacOS", exeName);
        var gameDataPath = Path.Combine(gamePath, "Contents", "Resources", "Data");
        args.PathToMetadata = Path.Combine(gameDataPath, "il2cpp_data", "Metadata", "global-metadata.dat");

        if (!File.Exists(args.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(args.PathToMetadata))
            throw new SoftException("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
                                    $"\t{args.PathToAssembly}\n" +
                                    $"\t{unityPlayerPath}\n" +
                                    $"\t{args.PathToMetadata}\n");

        Logger.VerboseNewline($"Found probable macOS game at path: {gamePath}. Attempting to get unity version...");

        var uv = Cpp2IlApi.DetermineUnityVersion(unityPlayerPath, gameDataPath);
        Logger.VerboseNewline($"First-attempt unity version detection gave: {uv}");

        if (uv == default)
        {
            Logger.Warn("Could not determine unity version, probably due to not running on windows and not having any assets files to determine it from. Enter unity version, if known, in the format of (xxxx.x.x), else nothing to fail: ");
            var userInputUv = Console.ReadLine();

            if (!string.IsNullOrEmpty(userInputUv))
                uv = UnityVersion.Parse(userInputUv);

            if (uv == default)
                throw new SoftException("Failed to determine unity version. If you're not running on windows, I need a globalgamemanagers file or a data.unity3d file, or you need to use the force options.");
        }

        args.UnityVersion = uv;

        if (args.UnityVersion.Major < 4)
        {
            Logger.WarnNewline($"Fail once: Unity version of provided executable is {args.UnityVersion}. This is probably not the correct version. Retrying with alternative method...");

            var readUnityVersionFrom = Path.Combine(gameDataPath, "globalgamemanagers");
            if (File.Exists(readUnityVersionFrom))
                args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(File.ReadAllBytes(readUnityVersionFrom));
            else
            {
                readUnityVersionFrom = Path.Combine(gameDataPath, "data.unity3d");
                using var stream = File.OpenRead(readUnityVersionFrom);

                args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(stream);
            }
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}");

        if (args.UnityVersion.Major <= 4)
            throw new SoftException($"Unable to determine a valid unity version (got {args.UnityVersion})");

        args.Valid = true;
    }

    private static void HandleLinuxGamePath(string gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
    {
        //Linux game.
        args.PathToAssembly = Path.Combine(gamePath, "GameAssembly.so");
        var exeName = Path.GetFileName(Directory.GetFiles(gamePath)
            .FirstOrDefault(f =>
                (f.EndsWith(".x86_64") || f.EndsWith(".x86")) &&
                !MiscUtils.BlacklistedExecutableFilenames.Any(f.EndsWith)));

        exeName = inputExeName ?? exeName;

        Logger.VerboseNewline($"Trying HandleLinuxGamePath as provided directory contains a GameAssembly.so, potential GA is {args.PathToAssembly} and executable {exeName}");

        if (exeName == null)
            throw new SoftException("Failed to locate any executable in the provided game directory. Make sure the path is correct, and if you *really* know what you're doing (and know it's not supported), use the force options, documented if you provide --help.");

        var exeNameNoExt = exeName.Replace(".x86_64", "").Replace(".x86", "");

        var unityPlayerPath = Path.Combine(gamePath, exeName);
        args.PathToMetadata = Path.Combine(gamePath, $"{exeNameNoExt}_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

        if (!File.Exists(args.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(args.PathToMetadata))
            throw new SoftException("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
                                    $"\t{args.PathToAssembly}\n" +
                                    $"\t{unityPlayerPath}\n" +
                                    $"\t{args.PathToMetadata}\n");

        Logger.VerboseNewline($"Found probable linux game at path: {gamePath}. Attempting to get unity version...");
        var gameDataPath = Path.Combine(gamePath, $"{exeNameNoExt}_Data");
        var uv = Cpp2IlApi.DetermineUnityVersion(unityPlayerPath, gameDataPath);
        Logger.VerboseNewline($"First-attempt unity version detection gave: {uv}");

        if (uv == default)
        {
            Logger.Warn("Could not determine unity version, probably due to not running on windows and not having any assets files to determine it from. Enter unity version, if known, in the format of (xxxx.x.x), else nothing to fail: ");
            var userInputUv = Console.ReadLine();

            if (!string.IsNullOrEmpty(userInputUv))
                uv = UnityVersion.Parse(userInputUv);

            if (uv == default)
                throw new SoftException("Failed to determine unity version. If you're not running on windows, I need a globalgamemanagers file or a data.unity3d file, or you need to use the force options.");
        }

        args.UnityVersion = uv;

        if (args.UnityVersion.Major < 4)
        {
            Logger.WarnNewline($"Fail once: Unity version of provided executable is {args.UnityVersion}. This is probably not the correct version. Retrying with alternative method...");

            var readUnityVersionFrom = Path.Combine(gameDataPath, "globalgamemanagers");
            if (File.Exists(readUnityVersionFrom))
                args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(File.ReadAllBytes(readUnityVersionFrom));
            else
            {
                readUnityVersionFrom = Path.Combine(gameDataPath, "data.unity3d");
                using var stream = File.OpenRead(readUnityVersionFrom);

                args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(stream);
            }
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}");

        if (args.UnityVersion.Major <= 4)
            throw new SoftException($"Unable to determine a valid unity version (got {args.UnityVersion})");

        args.Valid = true;
    }

    private static void HandleWindowsGamePath(string gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
    {
        //Windows game.
        args.PathToAssembly = Path.Combine(gamePath, "GameAssembly.dll");
        var exeName = Path.GetFileNameWithoutExtension(Directory.GetFiles(gamePath)
            .FirstOrDefault(f => f.EndsWith(".exe") && !MiscUtils.BlacklistedExecutableFilenames.Any(f.EndsWith)));

        exeName = inputExeName ?? exeName;

        Logger.VerboseNewline($"Trying HandleWindowsGamePath as provided path is a directory with no GameAssembly.so, potential GA is {args.PathToAssembly} and executable {exeName}");

        if (exeName == null)
            throw new SoftException("Failed to locate any executable in the provided game directory. Make sure the path is correct, and if you *really* know what you're doing (and know it's not supported), use the force options, documented if you provide --help.");

        var unityPlayerPath = Path.Combine(gamePath, $"{exeName}.exe");
        args.PathToMetadata = Path.Combine(gamePath, $"{exeName}_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

        if (!File.Exists(args.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(args.PathToMetadata))
            throw new SoftException("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
                                    $"\t{args.PathToAssembly}\n" +
                                    $"\t{unityPlayerPath}\n" +
                                    $"\t{args.PathToMetadata}\n");

        Logger.VerboseNewline($"Found probable windows game at path: {gamePath}. Attempting to get unity version...");
        var gameDataPath = Path.Combine(gamePath, $"{exeName}_Data");
        var uv = Cpp2IlApi.DetermineUnityVersion(unityPlayerPath, gameDataPath);
        Logger.VerboseNewline($"First-attempt unity version detection gave: {uv}");

        if (uv == default)
        {
            Logger.Warn("Could not determine unity version, probably due to not running on windows and not having any assets files to determine it from. Enter unity version, if known, in the format of (xxxx.x.x), else nothing to fail: ");
            var userInputUv = Console.ReadLine();

            if (!string.IsNullOrEmpty(userInputUv))
                uv = UnityVersion.Parse(userInputUv);

            if (uv == default)
                throw new SoftException("Failed to determine unity version. If you're not running on windows, I need a globalgamemanagers file or a data.unity3d file, or you need to use the force options.");
        }

        args.UnityVersion = uv;

        if (args.UnityVersion.Major < 4)
        {
            Logger.WarnNewline($"Fail once: Unity version of provided executable is {args.UnityVersion}. This is probably not the correct version. Retrying with alternative method...");

            var readUnityVersionFrom = Path.Combine(gameDataPath, "globalgamemanagers");
            if (File.Exists(readUnityVersionFrom))
                args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(File.ReadAllBytes(readUnityVersionFrom));
            else
            {
                readUnityVersionFrom = Path.Combine(gameDataPath, "data.unity3d");
                using var stream = File.OpenRead(readUnityVersionFrom);

                args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(stream);
            }
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}");

        if (args.UnityVersion.Major <= 4)
            throw new SoftException($"Unable to determine a valid unity version (got {args.UnityVersion})");

        args.Valid = true;
    }

    private static void HandleSingleApk(string gamePath, ref Cpp2IlRuntimeArgs args)
    {
        //APK
        //Metadata: assets/bin/Data/Managed/Metadata
        //Binary: lib/(armeabi-v7a)|(arm64-v8a)/libil2cpp.so

        Logger.VerboseNewline("Trying HandleSingleApk as provided path is an apk file");

        Logger.InfoNewline($"Attempting to extract required files from APK {gamePath}", "APK");

        using var stream = File.OpenRead(gamePath);
        using var zipArchive = new ZipArchive(stream);

        var globalMetadata = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("assets/bin/Data/Managed/Metadata/global-metadata.dat"));
        var binary = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("lib/x86_64/libil2cpp.so"));
        binary ??= zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("lib/x86/libil2cpp.so"));
        binary ??= zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("lib/arm64-v8a/libil2cpp.so"));
        binary ??= zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("lib/armeabi-v7a/libil2cpp.so"));

        var globalgamemanagers = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("assets/bin/Data/globalgamemanagers"));
        var dataUnity3d = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("assets/bin/Data/data.unity3d"));

        if (binary == null)
            throw new SoftException("Could not find libil2cpp.so inside the apk.");
        if (globalMetadata == null)
            throw new SoftException("Could not find global-metadata.dat inside the apk");
        if (globalgamemanagers == null && dataUnity3d == null)
            throw new SoftException("Could not find globalgamemanagers or data.unity3d inside the apk");

        var tempFileBinary = Path.GetTempFileName();
        var tempFileMeta = Path.GetTempFileName();

        PathsToDeleteOnExit.Add(tempFileBinary);
        PathsToDeleteOnExit.Add(tempFileMeta);

        Logger.InfoNewline($"Extracting APK/{binary.FullName} to {tempFileBinary}", "APK");
        binary.ExtractToFile(tempFileBinary, true);
        Logger.InfoNewline($"Extracting APK/{globalMetadata.FullName} to {tempFileMeta}", "APK");
        globalMetadata.ExtractToFile(tempFileMeta, true);

        args.PathToAssembly = tempFileBinary;
        args.PathToMetadata = tempFileMeta;

        if (globalgamemanagers != null)
        {
            Logger.InfoNewline("Reading globalgamemanagers to determine unity version...", "APK");
            var ggmBytes = new byte[0x40];
            using var ggmStream = globalgamemanagers.Open();

            // ReSharper disable once MustUseReturnValue
            ggmStream.Read(ggmBytes, 0, 0x40);

            args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(ggmBytes);
        }
        else
        {
            Logger.InfoNewline("Reading data.unity3d to determine unity version...", "APK");
            using var du3dStream = dataUnity3d!.Open();

            args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(du3dStream);
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}", "APK");

        args.Valid = true;
    }

    private static void HandleXapk(string gamePath, ref Cpp2IlRuntimeArgs args)
    {
        //XAPK file
        //Contains two APKs - one starting with `config.` and one with the package name
        //The config one is architecture-specific and so contains the binary
        //The other contains the metadata

        Logger.VerboseNewline("Trying HandleXapk as provided path is an xapk or apkm file");

        Logger.InfoNewline($"Attempting to extract required files from XAPK {gamePath}", "XAPK");

        using var xapkStream = File.OpenRead(gamePath);
        using var xapkZip = new ZipArchive(xapkStream);

        ZipArchiveEntry? configApk = null;
        var configApks = xapkZip.Entries.Where(e => e.FullName.Contains("config.") && e.FullName.EndsWith(".apk")).ToList();

        var instructionSetPreference = new string[] { "arm64_v8a", "arm64", "armeabi_v7a", "arm" };
        foreach (var instructionSet in instructionSetPreference)
        {
            configApk = configApks.FirstOrDefault(e => e.FullName.Contains(instructionSet));
            if (configApk != null)
                break;
        }

        //Try for base.apk, else find any apk that isn't the config apk
        var mainApk = xapkZip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".apk") && e.FullName.Contains("base.apk"))
                      ?? xapkZip.Entries.FirstOrDefault(e => e != configApk && e.FullName.EndsWith(".apk"));

        Logger.InfoNewline($"Identified APKs inside XAPK - config: {configApk?.FullName}, mainPackage: {mainApk?.FullName}", "XAPK");

        if (configApk == null)
            throw new SoftException("Could not find a config apk inside the XAPK");
        if (mainApk == null)
            throw new SoftException("Could not find a main apk inside the XAPK");

        using var configZip = new ZipArchive(configApk.Open());
        using var mainZip = new ZipArchive(mainApk.Open());
        var binary = configZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("libil2cpp.so"));
        var globalMetadata = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("global-metadata.dat"));

        var globalgamemanagers = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("globalgamemanagers"));
        var dataUnity3d = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("data.unity3d"));

        if (binary == null)
            throw new SoftException("Could not find libil2cpp.so inside the config APK");
        if (globalMetadata == null)
            throw new SoftException("Could not find global-metadata.dat inside the main APK");
        if (globalgamemanagers == null && dataUnity3d == null)
            throw new SoftException("Could not find globalgamemanagers or data.unity3d inside the main APK");

        var tempFileBinary = Path.GetTempFileName();
        var tempFileMeta = Path.GetTempFileName();

        PathsToDeleteOnExit.Add(tempFileBinary);
        PathsToDeleteOnExit.Add(tempFileMeta);

        Logger.InfoNewline($"Extracting XAPK/{configApk.Name}/{binary.FullName} to {tempFileBinary}", "XAPK");
        binary.ExtractToFile(tempFileBinary, true);
        Logger.InfoNewline($"Extracting XAPK{mainApk.Name}/{globalMetadata.FullName} to {tempFileMeta}", "XAPK");
        globalMetadata.ExtractToFile(tempFileMeta, true);

        args.PathToAssembly = tempFileBinary;
        args.PathToMetadata = tempFileMeta;

        if (globalgamemanagers != null)
        {
            Logger.InfoNewline("Reading globalgamemanagers to determine unity version...", "XAPK");
            var ggmBytes = new byte[0x40];
            using var ggmStream = globalgamemanagers.Open();

            // ReSharper disable once MustUseReturnValue
            ggmStream.Read(ggmBytes, 0, 0x40);

            args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(ggmBytes);
        }
        else
        {
            Logger.InfoNewline("Reading data.unity3d to determine unity version...", "XAPK");
            using var du3dStream = dataUnity3d!.Open();

            args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(du3dStream);
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}", "XAPK");

        args.Valid = true;
    }

    private static void HandleIpa(string gamePath, ref Cpp2IlRuntimeArgs args)
    {
        //IPA
        //Metadata: Payload/AppName.app/Data/Managed/Metadata/global-metadata.dat
        //Binary: Payload/AppName.app/Frameworks/UnityFramework.framework/UnityFramework
        //GlobalGameManager: Payload/AppName.app/Data/globalgamemanagers
        //Unity3d: Payload/AppName.app/Data/data.unity3d

        Logger.VerboseNewline("Trying HandleIpa as provided path is an ipa or tipa file");

        Logger.InfoNewline($"Attempting to extract required files from IPA {gamePath}", "IPA");

        using var stream = File.OpenRead(gamePath);
        using var zipArchive = new ZipArchive(stream);

        var globalMetadata = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("Data/Managed/Metadata/global-metadata.dat"));
        var binary = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("Frameworks/UnityFramework.framework/UnityFramework"));

        var globalgamemanagers = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("Data/globalgamemanagers"));
        var dataUnity3d = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("Data/data.unity3d"));

        if (binary == null)
            throw new SoftException("Could not find UnityFramework inside the ipa.");
        if (globalMetadata == null)
            throw new SoftException("Could not find global-metadata.dat inside the ipa.");
        if (globalgamemanagers == null && dataUnity3d == null)
            throw new SoftException("Could not find globalgamemanagers or unity3d inside the ipa.");

        var tempFileBinary = Path.GetTempFileName();
        var tempFileMeta = Path.GetTempFileName();

        PathsToDeleteOnExit.Add(tempFileBinary);
        PathsToDeleteOnExit.Add(tempFileMeta);

        Logger.InfoNewline($"Extracting IPA/{binary.FullName} to {tempFileBinary}", "IPA");
        binary.ExtractToFile(tempFileBinary, true);
        Logger.InfoNewline($"Extracting IPA/{globalMetadata.FullName} to {tempFileMeta}", "IPA");
        globalMetadata.ExtractToFile(tempFileMeta, true);

        args.PathToAssembly = tempFileBinary;
        args.PathToMetadata = tempFileMeta;

        if (globalgamemanagers != null)
        {
            Logger.InfoNewline("Reading globalgamemanagers to determine unity version...", "IPA");
            var ggmBytes = new byte[0x40];
            using var ggmStream = globalgamemanagers.Open();

            // ReSharper disable once MustUseReturnValue
            ggmStream.Read(ggmBytes, 0, 0x40);

            args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(ggmBytes);
        }
        else
        {
            Logger.InfoNewline("Reading data.unity3d to determine unity version...", "IPA");
            using var du3dStream = dataUnity3d!.Open();

            args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(du3dStream);
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}", "IPA");

        args.Valid = true;
    }

#if !NETFRAMEWORK
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "Cpp2IL.CommandLineArgs", "Cpp2IL")]
#endif
    private static Cpp2IlRuntimeArgs GetRuntimeOptionsFromCommandLine(string[] commandLine)
    {
        var parserResult = Parser.Default.ParseArguments<CommandLineArgs>(commandLine);

        if (parserResult is NotParsed<CommandLineArgs> notParsed && notParsed.Errors.Count() == 1 && notParsed.Errors.All(e => e.Tag is ErrorType.VersionRequestedError or ErrorType.HelpRequestedError))
            //Version or help requested
            Environment.Exit(0);

        if (parserResult is not Parsed<CommandLineArgs> { Value: { } options })
            throw new SoftException("Failed to parse command line arguments");

        ConsoleLogger.ShowVerbose = options.Verbose;

        Cpp2IlApi.Init();

        if (options.ListProcessors)
        {
            Logger.InfoNewline("Available processors:");
            foreach (var cpp2IlProcessingLayer in ProcessingLayerRegistry.AllProcessingLayers)
                Console.WriteLine($"  ID: {cpp2IlProcessingLayer.Id}   Name: {cpp2IlProcessingLayer.Name}");
            Environment.Exit(0);
        }

        if (options.ListOutputFormats)
        {
            Logger.InfoNewline("Available output formats:");
            foreach (var cpp2IlOutputFormat in OutputFormatRegistry.AllOutputFormats)
                Console.WriteLine($"  ID: {cpp2IlOutputFormat.OutputFormatId}   Name: {cpp2IlOutputFormat.OutputFormatName}");
            Environment.Exit(0);
        }

        if (!options.AreForceOptionsValid)
            throw new SoftException("Invalid force option configuration");

        Cpp2IlApi.ConfigureLib(false);

        var result = new Cpp2IlRuntimeArgs();

        if (options.ForcedBinaryPath == null)
        {
            ResolvePathsFromCommandLine(options.GamePath, options.ExeName, ref result);
        }
        else
        {
            Logger.WarnNewline("Using force options, I sure hope you know what you're doing!");
            result.PathToAssembly = options.ForcedBinaryPath!;
            result.PathToMetadata = options.ForcedMetadataPath!;
            result.UnityVersion = UnityVersion.Parse(options.ForcedUnityVersion!);
            result.Valid = true;
        }

        result.WasmFrameworkJsFile = options.WasmFrameworkFilePath;

        result.OutputRootDirectory = options.OutputRootDir;

        result.LowMemoryMode = options.LowMemoryMode;

        // if(string.IsNullOrEmpty(options.OutputFormatId))      // throw new SoftException("No output format specified, so nothing to do!");

        if (!string.IsNullOrEmpty(options.OutputFormatId))
        {
            try
            {
                result.OutputFormat = OutputFormatRegistry.GetFormat(options.OutputFormatId!);
                Logger.VerboseNewline($"Selected output format: {result.OutputFormat.OutputFormatName}");
            }
            catch (Exception e)
            {
                throw new SoftException(e.Message);
            }
        }

        try
        {
            result.ProcessingLayersToRun = options.ProcessorsToUse.Select(ProcessingLayerRegistry.GetById).ToList();
            if (result.ProcessingLayersToRun.Count > 0)
                Logger.VerboseNewline($"Selected processing layers: {string.Join(", ", result.ProcessingLayersToRun.Select(l => l.Name))}");
            else
                Logger.VerboseNewline("No processing layers requested");
        }
        catch (Exception e)
        {
            throw new SoftException(e.Message);
        }

        try
        {
            options.ProcessorConfigOptions.Select(c => c.Split('=')).ToList().ForEach(s => result.ProcessingLayerConfigurationOptions.Add(s[0], s[1]));
        }
        catch (IndexOutOfRangeException)
        {
            throw new SoftException("Processor config options must be in the format 'key=value'");
        }

        return result;
    }

    public static int Main(string[] args)
    {
        Console.WriteLine("===Cpp2IL by Samboy063===");
        Console.WriteLine("A Tool to Reverse Unity's \"il2cpp\" Build Process.");
        Console.WriteLine($"Version {Cpp2IlVersionString}\n");

        ConsoleLogger.Initialize();

        Logger.InfoNewline("Running on " + Environment.OSVersion.Platform);

        try
        {
            var runtimeArgs = GetRuntimeOptionsFromCommandLine(args);

            if (runtimeArgs.LowMemoryMode)
                //Force an early collection for all the zip shenanigans we may have just done
                GC.Collect();

            return MainWithArgs(runtimeArgs);
        }
        catch (SoftException e)
        {
            Logger.ErrorNewline($"Execution Failed: {e.Message}");
            return -1;
        }
#if !DEBUG
            catch (DllSaveException e)
            {
                Logger.ErrorNewline(e.ToString());
                Console.WriteLine();
                Console.WriteLine();
                Logger.ErrorNewline("Waiting for you to press enter - feel free to copy the error...");
                Console.ReadLine();

                return -1;
            }
            catch (LibCpp2ILInitializationException e)
            {
                Logger.ErrorNewline($"\n\n{e}\n\nWaiting for you to press enter - feel free to copy the error...");
                Console.ReadLine();
                return -1;
            }
#endif
    }

    public static int MainWithArgs(Cpp2IlRuntimeArgs runtimeArgs)
    {
        if (!runtimeArgs.Valid)
            throw new SoftException("Arguments have Valid = false");

        Cpp2IlApi.RuntimeOptions = runtimeArgs;

        var executionStart = DateTime.Now;

        runtimeArgs.OutputFormat?.OnOutputFormatSelected();

        GCSettings.LatencyMode = runtimeArgs.LowMemoryMode ? GCLatencyMode.Interactive : GCLatencyMode.SustainedLowLatency;

        if (runtimeArgs.WasmFrameworkJsFile != null)
            try
            {
                var frameworkJs = File.ReadAllText(runtimeArgs.WasmFrameworkJsFile);
                var remaps = WasmUtils.ExtractAndParseDynCallRemaps(frameworkJs);
                Logger.InfoNewline($"Parsed {remaps.Count} dynCall remaps from {runtimeArgs.WasmFrameworkJsFile}");
                WasmFile.RemappedDynCallFunctions = remaps;
            }
            catch (Exception e)
            {
                WasmFile.RemappedDynCallFunctions = null;
                Logger.WarnNewline($"Failed to parse dynCall remaps from Wasm Framework Javascript File: {e}. They will not be used, so you probably won't get method bodies!");
            }
        else
            WasmFile.RemappedDynCallFunctions = null;

        Cpp2IlApi.InitializeLibCpp2Il(runtimeArgs.PathToAssembly, runtimeArgs.PathToMetadata, runtimeArgs.UnityVersion);

        if (runtimeArgs.LowMemoryMode)
            GC.Collect();

        foreach (var (key, value) in runtimeArgs.ProcessingLayerConfigurationOptions)
            Cpp2IlApi.CurrentAppContext.PutExtraData(key, value);

        //Pre-process processing layers, allowing them to stop others from running
        Logger.InfoNewline("Pre-processing processing layers...");
        var layers = runtimeArgs.ProcessingLayersToRun.Clone();
        RunProcessingLayers(runtimeArgs, processingLayer => processingLayer.PreProcess(Cpp2IlApi.CurrentAppContext, layers));
        runtimeArgs.ProcessingLayersToRun = layers;

        //Run processing layers
        Logger.InfoNewline("Invoking processing layers...");
        RunProcessingLayers(runtimeArgs, processingLayer => processingLayer.Process(Cpp2IlApi.CurrentAppContext));

        var outputStart = DateTime.Now;

        if (runtimeArgs.OutputFormat != null)
        {
            if (runtimeArgs.LowMemoryMode)
                GC.Collect();

            Logger.InfoNewline($"Outputting as {runtimeArgs.OutputFormat.OutputFormatName} to {runtimeArgs.OutputRootDirectory}...");
            runtimeArgs.OutputFormat.DoOutput(Cpp2IlApi.CurrentAppContext, runtimeArgs.OutputRootDirectory);
            Logger.InfoNewline($"Finished outputting in {(DateTime.Now - outputStart).TotalMilliseconds}ms");
        }
        else
        {
            Logger.WarnNewline("No output format requested, so not outputting anything. The il2cpp game loaded properly though! (Hint: You probably want to specify an output format, try --output-as)");
        }

        // if (runtimeArgs.EnableMetadataGeneration)
        // Cpp2IlApi.GenerateMetadataForAllAssemblies(runtimeArgs.OutputRootDirectory);

        // if (runtimeArgs.EnableAnalysis)
        // Cpp2IlApi.PopulateConcreteImplementations();

        CleanupExtractedFiles();

        Cpp2IlPluginManager.CallOnFinish();

        Logger.InfoNewline($"Done. Total execution time: {(DateTime.Now - executionStart).TotalMilliseconds}ms");
        return 0;
    }

    private static void RunProcessingLayers(Cpp2IlRuntimeArgs runtimeArgs, Action<Cpp2IlProcessingLayer> run)
    {
        foreach (var processingLayer in runtimeArgs.ProcessingLayersToRun)
        {
            var processorStart = DateTime.Now;

            Logger.InfoNewline($"    {processingLayer.Name}...");

#if !DEBUG
                try
                {
#endif
            run(processingLayer);
#if !DEBUG
                }
                catch (Exception e)
                {
                    Logger.ErrorNewline($"Processing layer {processingLayer.Id} threw an exception: {e}");
                    Environment.Exit(1);
                }
#endif

            if (runtimeArgs.LowMemoryMode)
                GC.Collect();

            Logger.InfoNewline($"    {processingLayer.Name} finished in {(DateTime.Now - processorStart).TotalMilliseconds}ms");
        }
    }

    private static void CleanupExtractedFiles()
    {
        foreach (var p in PathsToDeleteOnExit)
        {
            try
            {
                Logger.InfoNewline($"Cleaning up {p}...");
                File.Delete(p);
            }
            catch (Exception)
            {
                //Ignore
            }
        }
    }
}
