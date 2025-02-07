using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AssetRipper.Primitives;
using Cpp2IL.Core.Logging;

namespace Cpp2IL.Core.Utils;

public static class PathUtils
{
    private static readonly List<string> PathsToDeleteOnExit = [];

    public static void CleanupExtractedFiles()
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

    public static void ResolvePathsFromCommandLine(string? gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
    {
        if (string.IsNullOrEmpty(gamePath))
            throw new Exception("No force options provided, and no game path was provided either. Please provide a game path or use the --force- options.");
        
        //Somehow the above doesn't tell .net that gamePath can't be null on net472, so we do this stupid thing to avoid nullable warnings
#if NET472
        gamePath = gamePath!;
#endif

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
                throw new Exception($"Could not find a valid unity game at {gamePath}");
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
            throw new Exception("Failed to locate any executable in the provided game directory. Make sure the path is correct, and if you *really* know what you're doing (and know it's not supported), use the force options, documented if you provide --help.");

        var unityPlayerPath = Path.Combine(gamePath, "Contents", "MacOS", exeName);
        var gameDataPath = Path.Combine(gamePath, "Contents", "Resources", "Data");
        args.PathToMetadata = Path.Combine(gameDataPath, "il2cpp_data", "Metadata", "global-metadata.dat");

        if (!File.Exists(args.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(args.PathToMetadata))
            throw new Exception("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
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
                throw new Exception("Failed to determine unity version. If you're not running on windows, I need a globalgamemanagers file or a data.unity3d file, or you need to use the force options.");
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
            throw new Exception($"Unable to determine a valid unity version (got {args.UnityVersion})");

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
            throw new Exception("Failed to locate any executable in the provided game directory. Make sure the path is correct, and if you *really* know what you're doing (and know it's not supported), use the force options, documented if you provide --help.");

        var exeNameNoExt = exeName.Replace(".x86_64", "").Replace(".x86", "");

        var unityPlayerPath = Path.Combine(gamePath, exeName);
        args.PathToMetadata = Path.Combine(gamePath, $"{exeNameNoExt}_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

        if (!File.Exists(args.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(args.PathToMetadata))
            throw new Exception("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
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
                throw new Exception("Failed to determine unity version. If you're not running on windows, I need a globalgamemanagers file or a data.unity3d file, or you need to use the force options.");
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
            throw new Exception($"Unable to determine a valid unity version (got {args.UnityVersion})");

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
            throw new Exception("Failed to locate any executable in the provided game directory. Make sure the path is correct, and if you *really* know what you're doing (and know it's not supported), use the force options, documented if you provide --help.");

        var unityPlayerPath = Path.Combine(gamePath, $"{exeName}.exe");
        args.PathToMetadata = Path.Combine(gamePath, $"{exeName}_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

        if (!File.Exists(args.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(args.PathToMetadata))
            throw new Exception("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
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
                throw new Exception("Failed to determine unity version. If you're not running on windows, I need a globalgamemanagers file or a data.unity3d file, or you need to use the force options.");
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
            throw new Exception($"Unable to determine a valid unity version (got {args.UnityVersion})");

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
            throw new Exception("Could not find libil2cpp.so inside the apk.");
        if (globalMetadata == null)
            throw new Exception("Could not find global-metadata.dat inside the apk");
        if (globalgamemanagers == null && dataUnity3d == null)
            throw new Exception("Could not find globalgamemanagers or data.unity3d inside the apk");

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

        var instructionSetPreference = new[] { "arm64_v8a", "arm64", "armeabi_v7a", "arm" };
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
            throw new Exception("Could not find a config apk inside the XAPK");
        if (mainApk == null)
            throw new Exception("Could not find a main apk inside the XAPK");

        using var configZip = new ZipArchive(configApk.Open());
        using var mainZip = new ZipArchive(mainApk.Open());
        var binary = configZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("libil2cpp.so"));
        var globalMetadata = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("global-metadata.dat"));

        var globalgamemanagers = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("globalgamemanagers"));
        var dataUnity3d = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("data.unity3d"));

        if (binary == null)
            throw new Exception("Could not find libil2cpp.so inside the config APK");
        if (globalMetadata == null)
            throw new Exception("Could not find global-metadata.dat inside the main APK");
        if (globalgamemanagers == null && dataUnity3d == null)
            throw new Exception("Could not find globalgamemanagers or data.unity3d inside the main APK");

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
            throw new Exception("Could not find UnityFramework inside the ipa.");
        if (globalMetadata == null)
            throw new Exception("Could not find global-metadata.dat inside the ipa.");
        if (globalgamemanagers == null && dataUnity3d == null)
            throw new Exception("Could not find globalgamemanagers or unity3d inside the ipa.");

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
}
