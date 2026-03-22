using System;
using System.IO;
using System.Linq;
using AssetRipper.Primitives;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;

namespace LibCpp2IL;

public sealed class LibCpp2IlContextBuilder
{
    private readonly LibCpp2IlContext _context = new(
        new LibCpp2IlMain.LibCpp2IlSettings // Snapshot settings at creation time.
        {
            AllowManualMetadataAndCodeRegInput = LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput,
            DisableMethodPointerMapping = LibCpp2IlMain.Settings.DisableMethodPointerMapping,
            DisableGlobalResolving = LibCpp2IlMain.Settings.DisableGlobalResolving,
        });

    private bool _metadataLoaded;
    private bool _binaryLoaded;

    public void LoadMetadata(byte[] metadataBytes, UnityVersion unityVersion)
    {
        var start = DateTime.Now;
        LibLogger.InfoNewline("Initializing Metadata...");

        var metadata = _context.Metadata = Il2CppMetadata.ReadFrom(metadataBytes, unityVersion);

        _context.Il2CppTypeHasNumMods5Bits = metadata.MetadataVersion >= 27.2f;

        LibLogger.InfoNewline($"Initialized Metadata in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

        // Legacy/static API compatibility: some in-binary structures still resolve via LibCpp2IlMain.Binary/TheMetadata
        // during binary initialization, so we must set metadata defaults before initializing the binary.
        LibCpp2IlMain.TheMetadata = metadata;
        LibCpp2IlMain.DefaultContext = _context;
        LibCpp2IlMain.Il2CppTypeHasNumMods5Bits = _context.Il2CppTypeHasNumMods5Bits;

        _metadataLoaded = true;
    }

    public void LoadBinary(byte[] binaryBytes)
    {
        if (!_metadataLoaded)
            throw new InvalidOperationException("Metadata must be loaded before the binary can be loaded.");

        var bin = _context.Binary = LibCpp2IlBinaryRegistry.CreateAndInit(binaryBytes, _context.Metadata);

        // Complete legacy/static initialization now that the binary exists.
        LibCpp2IlMain.Binary = bin;

        _binaryLoaded = true;
    }

    public void LoadBinary(Il2CppBinary binary)
    {
        if (!_metadataLoaded)
            throw new InvalidOperationException("Metadata must be loaded before the binary can be loaded.");

        binary.Init(_context.Metadata);

        _context.Binary = binary;

        // Complete legacy/static initialization now that the binary exists.
        LibCpp2IlMain.Binary = binary;

        _binaryLoaded = true;
    }

    public LibCpp2IlContext Build()
    {
        if (!_metadataLoaded)
            throw new InvalidOperationException("Metadata must be loaded before context can be built.");

        if (!_binaryLoaded)
            throw new InvalidOperationException("Binary must be loaded before context can be built.");

        DateTime start;
        if (!_context.Settings.DisableGlobalResolving && _context.MetadataVersion < 27)
        {
            start = DateTime.Now;
            LibLogger.Info("Mapping Globals...");
            LibCpp2IlGlobalMapper.MapGlobalIdentifiers(_context.Metadata, _context.Binary);
            LibLogger.InfoNewline($"OK ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");
        }

        if (!_context.Settings.DisableMethodPointerMapping)
        {
            start = DateTime.Now;
            LibLogger.Info("Mapping pointers to Il2CppMethodDefinitions...");
            foreach (var (method, ptr) in _context.Metadata.methodDefs.Select(method => (method, ptr: method.MethodPointer)))
            {
                if (!_context.MethodsByPtr.TryGetValue(ptr, out var list))
                    _context.MethodsByPtr[ptr] = list = [];

                list.Add(method);
            }

            LibLogger.InfoNewline($"Processed {_context.Metadata.methodDefs.Length} OK ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");
        }

        _context.ReflectionCache.Init(_context);

        return _context;
    }

    public static LibCpp2IlContext Build(byte[] binaryBytes, byte[] metadataBytes, UnityVersion unityVersion)
    {
        LibCpp2IlContextBuilder builder = new();

        builder.LoadMetadata(metadataBytes, unityVersion);
        builder.LoadBinary(binaryBytes);

        return builder.Build();
    }

    public static LibCpp2IlContext BuildFromFiles(string pePath, string metadataPath, UnityVersion unityVersion)
    {
        var metadataBytes = File.ReadAllBytes(metadataPath);
        var peBytes = File.ReadAllBytes(pePath);

        LibCpp2IlContextBuilder builder = new();

        builder.LoadMetadata(metadataBytes, unityVersion);
        builder.LoadBinary(peBytes);

        return builder.Build();
    }
}
