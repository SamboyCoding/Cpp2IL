using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AssetRipper.Primitives;
using LibCpp2IL.Elf;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;
using LibCpp2IL.NintendoSwitch;
using LibCpp2IL.Reflection;
using LibCpp2IL.Wasm;

namespace LibCpp2IL;

public static class LibCpp2IlMain
{
    private static readonly Regex UnityVersionRegex = new Regex(@"^[0-9]+\.[0-9]+\.[0-9]+[abcfxp][0-9]+$", RegexOptions.Compiled);

    public class LibCpp2IlSettings
    {
        public bool AllowManualMetadataAndCodeRegInput;
        public bool DisableMethodPointerMapping;
        public bool DisableGlobalResolving;
    }

    public static readonly LibCpp2IlSettings Settings = new();

    public static bool Il2CppTypeHasNumMods5Bits;
    public static float MetadataVersion = 24f;
    public static Il2CppBinary? Binary;
    public static Il2CppMetadata? TheMetadata;

    public static readonly Dictionary<ulong, List<Il2CppMethodDefinition>> MethodsByPtr = new();

    public static void Reset()
    {
        LibCpp2IlGlobalMapper.Reset();
        MethodsByPtr.Clear();
    }

    public static List<Il2CppMethodDefinition>? GetManagedMethodImplementationsAtAddress(ulong addr)
    {
        MethodsByPtr.TryGetValue(addr, out var ret);

        return ret;
    }

    public static MetadataUsage? GetAnyGlobalByAddress(ulong address)
    {
        if (MetadataVersion >= 27f)
            return LibCpp2IlGlobalMapper.CheckForPost27GlobalAt(address);

        //Pre-27
        var glob = GetLiteralGlobalByAddress(address);
        glob ??= GetMethodGlobalByAddress(address);
        glob ??= GetRawFieldGlobalByAddress(address);
        glob ??= GetRawTypeGlobalByAddress(address);

        return glob;
    }

    public static MetadataUsage? GetLiteralGlobalByAddress(ulong address)
    {
        if (MetadataVersion < 27f)
            return LibCpp2IlGlobalMapper.LiteralsByAddress.GetOrDefault(address);

        return GetAnyGlobalByAddress(address);
    }

    public static string? GetLiteralByAddress(ulong address)
    {
        var literal = GetLiteralGlobalByAddress(address);
        if (literal?.Type != MetadataUsageType.StringLiteral)
            return null;

        return literal.AsLiteral();
    }

    public static MetadataUsage? GetRawTypeGlobalByAddress(ulong address)
    {
        if (MetadataVersion < 27f)
            return LibCpp2IlGlobalMapper.TypeRefsByAddress.GetOrDefault(address);

        return GetAnyGlobalByAddress(address);
    }

    public static Il2CppTypeReflectionData? GetTypeGlobalByAddress(ulong address)
    {
        if (TheMetadata == null) return null;

        var typeGlobal = GetRawTypeGlobalByAddress(address);

        if (typeGlobal?.Type is not (MetadataUsageType.Type or MetadataUsageType.TypeInfo))
            return null;

        return typeGlobal.AsType();
    }

    public static MetadataUsage? GetRawFieldGlobalByAddress(ulong address)
    {
        if (MetadataVersion < 27f)
            return LibCpp2IlGlobalMapper.FieldRefsByAddress.GetOrDefault(address);
        return GetAnyGlobalByAddress(address);
    }

    public static Il2CppFieldDefinition? GetFieldGlobalByAddress(ulong address)
    {
        if (TheMetadata == null) return null;

        var typeGlobal = GetRawFieldGlobalByAddress(address);

        return typeGlobal?.AsField();
    }

    public static MetadataUsage? GetMethodGlobalByAddress(ulong address)
    {
        if (TheMetadata == null) return null;

        if (MetadataVersion < 27f)
            return LibCpp2IlGlobalMapper.MethodRefsByAddress.GetOrDefault(address);

        return GetAnyGlobalByAddress(address);
    }

    public static Il2CppMethodDefinition? GetMethodDefinitionByGlobalAddress(ulong address)
    {
        var global = GetMethodGlobalByAddress(address);

        if (global?.Type == MetadataUsageType.MethodRef)
            return global.AsGenericMethodRef().BaseMethod;

        return global?.AsMethod();
    }

    /// <summary>
    /// Initialize the metadata and PE from a pair of byte arrays.
    /// </summary>
    /// <param name="binaryBytes">The content of the GameAssembly.dll file.</param>
    /// <param name="metadataBytes">The content of the global-metadata.dat file</param>
    /// <param name="unityVersion">The unity version</param>
    /// <returns>True if the initialize succeeded, else false</returns>
    /// <throws><see cref="System.FormatException"/> if the metadata is invalid (bad magic number, bad version), or if the PE is invalid (bad header signature, bad magic number)<br/></throws>
    /// <throws><see cref="System.NotSupportedException"/> if the PE file specifies it is neither for AMD64 or i386 architecture</throws>
    public static bool Initialize(byte[] binaryBytes, byte[] metadataBytes, UnityVersion unityVersion)
    {
        LibCpp2IlReflection.ResetCaches();

        var start = DateTime.Now;

        LibLogger.InfoNewline("Initializing Metadata...");

        TheMetadata = Il2CppMetadata.ReadFrom(metadataBytes, unityVersion);

        Il2CppTypeHasNumMods5Bits = MetadataVersion >= 27.2f;

        if (TheMetadata == null)
            return false;

        LibLogger.InfoNewline($"Initialized Metadata in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

        Binary = LibCpp2IlBinaryRegistry.CreateAndInit(binaryBytes, TheMetadata);

        if (!Settings.DisableGlobalResolving && MetadataVersion < 27)
        {
            start = DateTime.Now;
            LibLogger.Info("Mapping Globals...");
            LibCpp2IlGlobalMapper.MapGlobalIdentifiers(TheMetadata, Binary);
            LibLogger.InfoNewline($"OK ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");
        }

        if (!Settings.DisableMethodPointerMapping)
        {
            start = DateTime.Now;
            LibLogger.Info("Mapping pointers to Il2CppMethodDefinitions...");
            var i = 0;
            foreach (var (method, ptr) in TheMetadata.methodDefs.Select(method => (method, ptr: method.MethodPointer)))
            {
                if (!MethodsByPtr.ContainsKey(ptr))
                    MethodsByPtr[ptr] = [];

                MethodsByPtr[ptr].Add(method);
                i++;
            }

            LibLogger.InfoNewline($"Processed {i} OK ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");
        }

        LibCpp2IlReflection.InitCaches();

        return true;
    }

    /// <summary>
    /// Initialize the metadata and PE from their respective file locations.
    /// </summary>
    /// <param name="pePath">The path to the GameAssembly.dll file</param>
    /// <param name="metadataPath">The path to the global-metadata.dat file</param>
    /// <param name="unityVersion">The unity version, split on periods, with the patch version (e.g. f1) stripped out. For example, [2018, 2, 0]</param>
    /// <returns>True if the initialize succeeded, else false</returns>
    /// <throws><see cref="System.FormatException"/> if the metadata is invalid (bad magic number, bad version), or if the PE is invalid (bad header signature, bad magic number)<br/></throws>
    /// <throws><see cref="System.NotSupportedException"/> if the PE file specifies it is neither for AMD64 or i386 architecture</throws>
    public static bool LoadFromFile(string pePath, string metadataPath, UnityVersion unityVersion)
    {
        var metadataBytes = File.ReadAllBytes(metadataPath);
        var peBytes = File.ReadAllBytes(pePath);

        return Initialize(peBytes, metadataBytes, unityVersion);
    }

    /// <summary>
    /// Attempts to determine the Unity version from the given binary path and game data path
    /// </summary>
    /// <param name="unityPlayerPath">The path to the unity player executable - either the executable itself or [lib]unityplayer[.dll]</param>
    /// <param name="gameDataPath">The path to the GameName_Data folder, from which assets files can be read.</param>
    /// <returns>A valid unity version if one can be read, else 0.0.0a0</returns>
    public static UnityVersion DetermineUnityVersion(string? unityPlayerPath, string? gameDataPath)
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT && !string.IsNullOrEmpty(unityPlayerPath))
        {
            LibLogger.VerboseNewline($"DetermineUnityVersion: Running on windows so have FileVersionInfo, trying to pull version from unity player {unityPlayerPath}");
            var unityVer = FileVersionInfo.GetVersionInfo(unityPlayerPath);

            if (unityVer.FileMajorPart > 0)
                return new UnityVersion((ushort)unityVer.FileMajorPart, (ushort)unityVer.FileMinorPart, (ushort)unityVer.FileBuildPart);

            LibLogger.VerboseNewline($"DetermineUnityVersion: FileVersionInfo gave useless result, falling back to other methods");
        }

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
}
