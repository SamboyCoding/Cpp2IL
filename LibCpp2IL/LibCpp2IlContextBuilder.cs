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
            MetadataFixupFunc = LibCpp2IlMain.Settings.MetadataFixupFunc
        });

    private bool _metadataLoaded;
    private bool _binaryLoaded;

    public void LoadMetadata(byte[] metadataBytes, UnityVersion unityVersion)
    {
        var start = DateTime.Now;
        LibLogger.InfoNewline("Initializing Metadata...");

        Il2CppMetadata metadata;
        try
        {
            metadata = Il2CppMetadata.ReadFrom(metadataBytes, unityVersion);
        }
        catch (Exception e) when (_context.Settings.MetadataFixupFunc is { } fixupFunc)
        {
            try
            {
                LibLogger.WarnNewline("Metadata read failed, but a fixup function is registered. Calling fixup function and then will attempt to read again...");
                
                var fixedBytes = fixupFunc(metadataBytes, unityVersion);
                
                if(fixedBytes == null)
                    throw new Exception("Metadata fixup function returned null, cannot proceed with metadata loading. Original exception follows.", e);
                
                metadata = Il2CppMetadata.ReadFrom(fixedBytes, unityVersion);
                
                LibLogger.InfoNewline("Metadata read succeeded after fixup.");
            }
            catch (Exception ex2)
            {
                throw new Exception("Failed to read metadata, even after attempted fixup.", ex2);
            }
        }

        LibLogger.InfoNewline($"Initialized Metadata in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

        LoadMetadata(metadata);
    }

    public void LoadMetadata(Il2CppMetadata metadata)
    {
        _context.Metadata = metadata;
        metadata.SetOwningContext(_context);

        _metadataLoaded = true;
    }

    public void LoadBinary(byte[] binaryBytes)
    {
        if (!_metadataLoaded)
            throw new InvalidOperationException("Metadata must be loaded before the binary can be loaded.");

        LibCpp2IlBinaryRegistry.CreateAndInit(binaryBytes, _context);

        _binaryLoaded = true;
    }

    public void LoadBinary(Il2CppBinary binary)
    {
        if (!_metadataLoaded)
            throw new InvalidOperationException("Metadata must be loaded before the binary can be loaded.");

        binary.Init(_context);

        _binaryLoaded = true;
    }

    public LibCpp2IlContext Build()
    {
        if (!_metadataLoaded)
            throw new InvalidOperationException("Metadata must be loaded before context can be built.");

        if (!_binaryLoaded)
            throw new InvalidOperationException("Binary must be loaded before context can be built.");

        _context.ReflectionCache.Init(_context);

        if (!_context.Settings.DisableGlobalResolving && _context.Metadata.MetadataVersion < 27)
        {
            var start = DateTime.Now;
            LibLogger.Info("Mapping Globals...");
            _context.MapGlobalIdentifiers();
            LibLogger.InfoNewline($"OK ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");
        }

        if (!_context.Settings.DisableMethodPointerMapping)
        {
            var start = DateTime.Now;
            LibLogger.Info("Mapping pointers to Il2CppMethodDefinitions...");
            foreach (var (method, ptr) in _context.Metadata.methodDefs.Select(method => (method, ptr: method.MethodPointer)))
            {
                if (!_context.MethodsByPtr.TryGetValue(ptr, out var list))
                    _context.MethodsByPtr[ptr] = list = [];

                list.Add(method);
            }

            LibLogger.InfoNewline($"Processed {_context.Metadata.methodDefs.Length} OK ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");
        }

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

        return Build(peBytes, metadataBytes, unityVersion);
    }
}
