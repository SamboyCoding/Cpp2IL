using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AssetRipper.Primitives;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL;

public static class LibCpp2IlMain
{
    public delegate byte[]? MetadataFixupFunc(byte[] originalBytes, UnityVersion unityVersion);

    private static readonly Regex UnityVersionRegex = new Regex(@"^[0-9]+\.[0-9]+\.[0-9]+[abcfxp][0-9]+$", RegexOptions.Compiled);

    public class LibCpp2IlSettings
    {
        public bool AllowManualMetadataAndCodeRegInput;
        public bool DisableMethodPointerMapping;
        public bool DisableGlobalResolving;
        public MetadataFixupFunc? MetadataFixupFunc; //If set, this will be called if metadata fails to parse, to allow you to attempt to fix it.
    }

    public static readonly LibCpp2IlSettings Settings = new();

    /// <summary>
    /// Initialize the metadata and binary from a pair of byte arrays, returning a context.
    /// </summary>
    public static LibCpp2IlContext InitializeAsContext(byte[] binaryBytes, byte[] metadataBytes, UnityVersion unityVersion)
        => LibCpp2IlContextBuilder.Build(binaryBytes, metadataBytes, unityVersion);

    /// <summary>
    /// Initialize the metadata and binary from file paths, returning a context.
    /// </summary>
    public static LibCpp2IlContext LoadFromFileAsContext(string pePath, string metadataPath, UnityVersion unityVersion)
        => LibCpp2IlContextBuilder.BuildFromFiles(pePath, metadataPath, unityVersion);

    /// <summary>
    /// Attempts to determine the Unity version from the given binary path and game data path
    /// </summary>
    /// <param name="unityPlayerPath">The path to the unity player executable - either the executable itself or [lib]unityplayer[.dll]</param>
    /// <param name="gameDataPath">The path to the GameName_Data folder, from which assets files can be read.</param>
    /// <returns>A valid unity version if one can be read, else 0.0.0a0</returns>
    public static UnityVersion DetermineUnityVersion(string? unityPlayerPath, string? gameDataPath)
    {
        //We prefer pulling from assets because it gives a full version number (i.e. including what is usually an f1 at the end)
        //But we can fall back to the unity player if we have to (and are on windows)
        if (!string.IsNullOrEmpty(gameDataPath))
        {
            LibLogger.VerboseNewline($"DetermineUnityVersion: Have game data path {gameDataPath}, trying to pull version from globalgamemanagers or data.unity3d");

            //Globalgamemanagers
            var globalgamemanagersPath = Path.Combine(gameDataPath, "globalgamemanagers");
            if (File.Exists(globalgamemanagersPath))
            {
                LibLogger.VerboseNewline($"DetermineUnityVersion: globalgamemanagers exists, pulling version from it");

                var ggmBytes = File.ReadAllBytes(globalgamemanagersPath);
                return GetVersionFromGlobalGameManagers(ggmBytes);
            }

            //Data.unity3d
            var dataPath = Path.Combine(gameDataPath, "data.unity3d");
            if (File.Exists(dataPath))
            {
                LibLogger.VerboseNewline($"DetermineUnityVersion: data.unity3d exists, pulling version from it");

                using var dataStream = File.OpenRead(dataPath);
                return GetVersionFromDataUnity3D(dataStream);
            }

            LibLogger.VerboseNewline($"DetermineUnityVersion: No globalgamemanagers or data.unity3d found in game data path.");
        }

        if (Environment.OSVersion.Platform == PlatformID.Win32NT && !string.IsNullOrEmpty(unityPlayerPath))
        {
            LibLogger.VerboseNewline($"DetermineUnityVersion: Running on windows so have FileVersionInfo, trying to pull version from unity player {unityPlayerPath}");
            var unityVer = FileVersionInfo.GetVersionInfo(unityPlayerPath);

            if (unityVer.FileMajorPart > 0)
                return new UnityVersion((ushort)unityVer.FileMajorPart, (ushort)unityVer.FileMinorPart, (ushort)unityVer.FileBuildPart);

            LibLogger.VerboseNewline($"DetermineUnityVersion: FileVersionInfo gave useless result.");
        }

        LibLogger.VerboseNewline($"DetermineUnityVersion: All methods to determine unity version failed!");

        return default;
    }

    /// <summary>
    /// Attempts to determine the Unity version from the given globalgamemanagers file
    /// </summary>
    /// <param name="ggmBytes">The bytes making up the globalgamemanagers asset file</param>
    /// <returns>A valid unity version if one can be read, else 0.0.0a0</returns>
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

        if (!UnityVersionRegex.IsMatch(unityVer))
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

    /// <summary>
    /// Attempts to determine the Unity version from the given data.unity3d file
    /// </summary>
    /// <param name="fileStream">A stream referencing the data.unity3d file. A stream is used instead of a byte array because these files can be very large. Only the first 30-or-so bytes are used.</param>
    /// <returns>A valid unity version if one can be read, else 0.0.0a0</returns>
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
        {
            if (fileStream.Read(new byte[0x12], 0, 0x12) != 0x12)
                throw new("Failed to seek to 0x12 in data.unity3d");
        }

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

    #region Legacy static API — kept for backwards compatibility during migration

    // These fields exist solely to allow external consumers to keep working during the transition to context-based APIs.
    // New code should use LibCpp2IlContext directly.

    private static LibCpp2IlContext? _defaultContext;

    [Obsolete("Use LibCpp2IlContext instead.")]
    public static LibCpp2IlContext DefaultContext
    {
        get => _defaultContext ?? throw new InvalidOperationException("LibCpp2IL is not Initialized");
        set => _defaultContext = value;
    }

    [Obsolete("Use LibCpp2IlContext.Binary instead.")]
    public static Il2CppBinary Binary => DefaultContext.Binary;

    [Obsolete("Use LibCpp2IlContext.Metadata instead.")]
    public static Il2CppMetadata TheMetadata => DefaultContext.Metadata;

    [Obsolete("Use context.Il2CppTypeHasNumMods5Bits instead.")]
    public static bool Il2CppTypeHasNumMods5Bits => DefaultContext.Il2CppTypeHasNumMods5Bits;

    [Obsolete("Use LibCpp2IlContext.MetadataVersion instead.")]
    public static float MetadataVersion => DefaultContext.Metadata.MetadataVersion;

    [Obsolete("Use LibCpp2IlContext.MethodsByPtr instead.")]
    public static Dictionary<ulong, List<Il2CppMethodDefinition>> MethodsByPtr => DefaultContext.MethodsByPtr;

    [Obsolete("Use LibCpp2IlContextBuilder directly.")]
    public static bool Initialize(byte[] binaryBytes, byte[] metadataBytes, UnityVersion unityVersion)
    {
        _defaultContext = LibCpp2IlContextBuilder.Build(binaryBytes, metadataBytes, unityVersion);

        return true;
    }

    [Obsolete("Use LibCpp2IlContextBuilder directly.")]
    public static bool LoadFromFile(string pePath, string metadataPath, UnityVersion unityVersion)
    {
        _defaultContext = LibCpp2IlContextBuilder.BuildFromFiles(pePath, metadataPath, unityVersion);

        return true;
    }

    [Obsolete("Use context.GetManagedMethodImplementationsAtAddress instead.")]
    public static List<Il2CppMethodDefinition>? GetManagedMethodImplementationsAtAddress(ulong addr) => DefaultContext.GetManagedMethodImplementationsAtAddress(addr);

    [Obsolete("Use context.GetAnyGlobalByAddress instead.")]
    public static MetadataUsage? GetAnyGlobalByAddress(ulong address) => DefaultContext.GetAnyGlobalByAddress(address);

    [Obsolete("Use context.GetLiteralGlobalByAddress instead.")]
    public static MetadataUsage? GetLiteralGlobalByAddress(ulong address) => DefaultContext.GetLiteralGlobalByAddress(address);

    [Obsolete("Use context.GetLiteralByAddress instead.")]
    public static string? GetLiteralByAddress(ulong address) => DefaultContext.GetLiteralByAddress(address);

    [Obsolete("Use context.GetRawTypeGlobalByAddress instead.")]
    public static MetadataUsage? GetRawTypeGlobalByAddress(ulong address) => DefaultContext.GetRawTypeGlobalByAddress(address);

    [Obsolete("Use context.GetTypeGlobalByAddress instead.")]
    public static Il2CppTypeReflectionData? GetTypeGlobalByAddress(ulong address) => DefaultContext.GetTypeGlobalByAddress(address);

    [Obsolete("Use context.GetRawFieldGlobalByAddress instead.")]
    public static MetadataUsage? GetRawFieldGlobalByAddress(ulong address) => DefaultContext.GetRawFieldGlobalByAddress(address);

    [Obsolete("Use context.GetFieldGlobalByAddress instead.")]
    public static Il2CppFieldDefinition? GetFieldGlobalByAddress(ulong address) => DefaultContext.GetFieldGlobalByAddress(address);

    [Obsolete("Use context.GetMethodGlobalByAddress instead.")]
    public static MetadataUsage? GetMethodGlobalByAddress(ulong address) => DefaultContext.GetMethodGlobalByAddress(address);

    [Obsolete("Use context.GetMethodDefinitionByGlobalAddress instead.")]
    public static Il2CppMethodDefinition? GetMethodDefinitionByGlobalAddress(ulong address) => DefaultContext.GetMethodDefinitionByGlobalAddress(address);

    [Obsolete("Use LibCpp2IlContext instead.")]
    public static void Reset()
    {
        _defaultContext = null;
    }

    #endregion
}
