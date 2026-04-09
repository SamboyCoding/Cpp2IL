using System.Collections.Generic;
using System.Linq;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL;

/// <summary>
/// Represents a single initialized IL2CPP application (binary + metadata) and holds state that was historically global/static.
/// </summary>
public sealed class LibCpp2IlContext
{
    public LibCpp2IlMain.LibCpp2IlSettings Settings { get; }

    public bool Il2CppTypeHasNumMods5Bits { get; internal set; }

    public Il2CppBinary Binary { get; internal set; } = null!;
    public Il2CppMetadata Metadata { get; internal set; } = null!;

    public float MetadataVersion => Metadata.MetadataVersion;

    public Dictionary<ulong, List<Il2CppMethodDefinition>> MethodsByPtr { get; } = new();

    public LibCpp2IlReflectionCache ReflectionCache { get; } = new();

    // Global mapper state (was static on LibCpp2IlGlobalMapper)
    internal List<MetadataUsage> TypeRefs = [];
    internal List<MetadataUsage> MethodRefs = [];
    internal List<MetadataUsage> FieldRefs = [];
    internal List<MetadataUsage> Literals = [];

    internal Dictionary<ulong, MetadataUsage> TypeRefsByAddress = new();
    internal Dictionary<ulong, MetadataUsage> MethodRefsByAddress = new();
    internal Dictionary<ulong, MetadataUsage> FieldRefsByAddress = new();
    internal Dictionary<ulong, MetadataUsage> LiteralsByAddress = new();

    internal LibCpp2IlContext(LibCpp2IlMain.LibCpp2IlSettings settings)
    {
        Settings = settings;
    }

    public List<Il2CppMethodDefinition>? GetManagedMethodImplementationsAtAddress(ulong addr)
        => MethodsByPtr.TryGetValue(addr, out var ret) ? ret : null;

    internal void MapGlobalIdentifiers()
    {
        if (MetadataVersion < 27f)
            MapGlobalIdentifiersPre27();
        // Post-27 is a no-op - globals are decoded on demand
    }

    private void MapGlobalIdentifiersPre27()
    {
        var metadata = Metadata;
        var cppAssembly = Binary;

        //We non-null assert here because this function is only called pre-27, when this is guaranteed to be non-null
        TypeRefs = metadata.metadataUsageDic![(uint)MetadataUsageType.TypeInfo]
            .Select(kvp => new MetadataUsage(MetadataUsageType.Type, cppAssembly.GetRawMetadataUsage(kvp.Key), kvp.Value, Binary, Metadata))
            .ToList();

        TypeRefs.AddRange(metadata.metadataUsageDic[(uint)MetadataUsageType.Type]
            .Select(kvp => new MetadataUsage(MetadataUsageType.Type, cppAssembly.GetRawMetadataUsage(kvp.Key), kvp.Value, Binary, Metadata))
        );

        MethodRefs = metadata.metadataUsageDic[(uint)MetadataUsageType.MethodDef]
            .Select(kvp => new MetadataUsage(MetadataUsageType.MethodDef, cppAssembly.GetRawMetadataUsage(kvp.Key), kvp.Value, Binary, Metadata))
            .ToList();

        FieldRefs = metadata.metadataUsageDic[(uint)MetadataUsageType.FieldInfo]
            .Select(kvp => new MetadataUsage(MetadataUsageType.FieldInfo, cppAssembly.GetRawMetadataUsage(kvp.Key), kvp.Value, Binary, Metadata))
            .ToList();

        Literals = metadata.metadataUsageDic[(uint)MetadataUsageType.StringLiteral]
            .Select(kvp => new MetadataUsage(MetadataUsageType.StringLiteral, cppAssembly.GetRawMetadataUsage(kvp.Key), kvp.Value, Binary, Metadata)).ToList();

        foreach (var (metadataUsageIdx, methodSpecIdx) in metadata.metadataUsageDic[(uint)MetadataUsageType.MethodRef])
        {
            MethodRefs.Add(new MetadataUsage(MetadataUsageType.MethodRef, cppAssembly.GetRawMetadataUsage(metadataUsageIdx), methodSpecIdx, Binary, Metadata));
        }

        foreach (var globalIdentifier in TypeRefs)
            TypeRefsByAddress[globalIdentifier.Offset] = globalIdentifier;

        foreach (var globalIdentifier in MethodRefs)
            MethodRefsByAddress[globalIdentifier.Offset] = globalIdentifier;

        foreach (var globalIdentifier in FieldRefs)
            FieldRefsByAddress[globalIdentifier.Offset] = globalIdentifier;

        foreach (var globalIdentifier in Literals)
            LiteralsByAddress[globalIdentifier.Offset] = globalIdentifier;
    }

    public MetadataUsage? CheckForPost27GlobalAt(ulong address)
    {
        if (!Binary.TryMapVirtualAddressToRaw(address, out var raw) || raw >= Binary.RawLength)
            return null;

        var encoded = Binary.ReadPointerAtVirtualAddress(address);
        var metadataUsage = MetadataUsage.DecodeMetadataUsage(encoded, address, Binary, Metadata);

        if (metadataUsage?.IsValid != true)
            return null;

        return metadataUsage;
    }

    public MetadataUsage? GetAnyGlobalByAddress(ulong address)
    {
        if (MetadataVersion >= 27f)
            return CheckForPost27GlobalAt(address);

        var glob = GetLiteralGlobalByAddress(address);
        glob ??= GetMethodGlobalByAddress(address);
        glob ??= GetRawFieldGlobalByAddress(address);
        glob ??= GetRawTypeGlobalByAddress(address);

        return glob;
    }

    public MetadataUsage? GetLiteralGlobalByAddress(ulong address)
        => MetadataVersion < 27f ? LiteralsByAddress.GetOrDefault(address) : GetAnyGlobalByAddress(address);

    public string? GetLiteralByAddress(ulong address)
    {
        var literal = GetLiteralGlobalByAddress(address);
        if (literal?.Type != MetadataUsageType.StringLiteral)
            return null;

        return literal.AsLiteral();
    }

    public MetadataUsage? GetRawTypeGlobalByAddress(ulong address)
        => MetadataVersion < 27f ? TypeRefsByAddress.GetOrDefault(address) : GetAnyGlobalByAddress(address);

    public Il2CppTypeReflectionData? GetTypeGlobalByAddress(ulong address)
    {
        var typeGlobal = GetRawTypeGlobalByAddress(address);

        if (typeGlobal?.Type is not (MetadataUsageType.Type or MetadataUsageType.TypeInfo))
            return null;

        return typeGlobal.AsType();
    }

    public MetadataUsage? GetRawFieldGlobalByAddress(ulong address)
        => MetadataVersion < 27f ? FieldRefsByAddress.GetOrDefault(address) : GetAnyGlobalByAddress(address);

    public Il2CppFieldDefinition? GetFieldGlobalByAddress(ulong address)
        => GetRawFieldGlobalByAddress(address)?.AsField();

    public MetadataUsage? GetMethodGlobalByAddress(ulong address)
        => MetadataVersion < 27f ? MethodRefsByAddress.GetOrDefault(address) : GetAnyGlobalByAddress(address);

    public Il2CppMethodDefinition? GetMethodDefinitionByGlobalAddress(ulong address)
    {
        var global = GetMethodGlobalByAddress(address);

        if (global?.Type == MetadataUsageType.MethodRef)
            return global.AsGenericMethodRef().BaseMethod;

        return global?.AsMethod();
    }
}
