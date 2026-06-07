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

    public bool Il2CppTypeHasNumMods5Bits => Metadata.MetadataVersion >= 27.2f;

    public Il2CppBinary Binary { get; internal set; } = null!;
    public Il2CppMetadata Metadata { get; internal set; } = null!;

    public Dictionary<ulong, List<Il2CppMethodDefinition>> MethodsByPtr { get; } = new();

    public LibCpp2IlReflectionCache ReflectionCache { get; } = new();

    // Global mapper state
    internal List<MetadataUsage> TypeRefs = [];
    internal List<MetadataUsage> MethodRefs = [];
    internal List<MetadataUsage> FieldRefs = [];
    internal List<MetadataUsage> Literals = [];

    internal readonly Dictionary<ulong, MetadataUsage> TypeRefsByAddress = new();
    internal readonly Dictionary<ulong, MetadataUsage> MethodRefsByAddress = new();
    internal readonly Dictionary<ulong, MetadataUsage> FieldRefsByAddress = new();
    internal readonly Dictionary<ulong, MetadataUsage> LiteralsByAddress = new();

    internal LibCpp2IlContext(LibCpp2IlMain.LibCpp2IlSettings settings)
    {
        Settings = settings;
    }

    public List<Il2CppMethodDefinition>? GetManagedMethodImplementationsAtAddress(ulong addr)
        => MethodsByPtr.TryGetValue(addr, out var ret) ? ret : null;

    internal void MapGlobalIdentifiers()
    {
        if (Metadata.MetadataVersion < 27f)
            MapGlobalIdentifiersPre27();

        // Post-27 is a no-op
    }

    private void MapGlobalIdentifiersPre27()
    {
        //Type 1 => TypeInfo
        //Type 2 => Il2CppType
        //Type 3 => MethodDef
        //Type 4 => FieldInfo
        //Type 5 => StringLiteral
        //Type 6 => MethodRef

        //Type references

        //We non-null assert here because this function is only called pre-27, when this is guaranteed to be non-null
        TypeRefs = Metadata.metadataUsageDic![(uint)MetadataUsageType.TypeInfo]
            .Select(kvp => new MetadataUsage(MetadataUsageType.Type, Binary.GetRawMetadataUsage(kvp.Key), kvp.Value, this))
            .ToList();

        //More type references
        TypeRefs.AddRange(Metadata.metadataUsageDic[(uint)MetadataUsageType.Type]
            .Select(kvp => new MetadataUsage(MetadataUsageType.Type, Binary.GetRawMetadataUsage(kvp.Key), kvp.Value, this))
        );

        //Method references
        MethodRefs = Metadata.metadataUsageDic[(uint)MetadataUsageType.MethodDef]
            .Select(kvp => new MetadataUsage(MetadataUsageType.MethodDef, Binary.GetRawMetadataUsage(kvp.Key), kvp.Value, this))
            .ToList();

        //Field references
        FieldRefs = Metadata.metadataUsageDic[(uint)MetadataUsageType.FieldInfo]
            .Select(kvp => new MetadataUsage(MetadataUsageType.FieldInfo, Binary.GetRawMetadataUsage(kvp.Key), kvp.Value, this))
            .ToList();

        //Literals
        Literals = Metadata.metadataUsageDic[(uint)MetadataUsageType.StringLiteral]
            .Select(kvp => new MetadataUsage(MetadataUsageType.StringLiteral, Binary.GetRawMetadataUsage(kvp.Key), kvp.Value, this)).ToList();

        //Generic method references
        foreach (var (metadataUsageIdx, methodSpecIdx) in Metadata.metadataUsageDic[(uint)MetadataUsageType.MethodRef]) //kIl2CppMetadataUsageMethodRef
        {
            MethodRefs.Add(new MetadataUsage(MetadataUsageType.MethodRef, Binary.GetRawMetadataUsage(metadataUsageIdx), methodSpecIdx, this));
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
        var metadataUsage = MetadataUsage.DecodeMetadataUsage(encoded, address, this);

        if (metadataUsage?.IsValid != true)
            return null;

        return metadataUsage;
    }

    public MetadataUsage? GetAnyGlobalByAddress(ulong address)
    {
        if (Metadata.MetadataVersion >= 27f)
            return CheckForPost27GlobalAt(address);

        var glob = GetLiteralGlobalByAddress(address);
        glob ??= GetMethodGlobalByAddress(address);
        glob ??= GetRawFieldGlobalByAddress(address);
        glob ??= GetRawTypeGlobalByAddress(address);

        return glob;
    }

    public MetadataUsage? GetLiteralGlobalByAddress(ulong address)
        => Metadata.MetadataVersion < 27f ? LiteralsByAddress.GetOrDefault(address) : GetAnyGlobalByAddress(address);

    public string? GetLiteralByAddress(ulong address)
    {
        var literal = GetLiteralGlobalByAddress(address);
        if (literal?.Type != MetadataUsageType.StringLiteral)
            return null;

        return literal.AsLiteral();
    }

    public MetadataUsage? GetRawTypeGlobalByAddress(ulong address)
        => Metadata.MetadataVersion < 27f ? TypeRefsByAddress.GetOrDefault(address) : GetAnyGlobalByAddress(address);

    public Il2CppTypeReflectionData? GetTypeGlobalByAddress(ulong address)
    {
        var typeGlobal = GetRawTypeGlobalByAddress(address);

        if (typeGlobal?.Type is not (MetadataUsageType.Type or MetadataUsageType.TypeInfo))
            return null;

        return typeGlobal.AsType();
    }

    public MetadataUsage? GetRawFieldGlobalByAddress(ulong address)
        => Metadata.MetadataVersion < 27f ? FieldRefsByAddress.GetOrDefault(address) : GetAnyGlobalByAddress(address);

    public Il2CppFieldDefinition? GetFieldGlobalByAddress(ulong address)
        => GetRawFieldGlobalByAddress(address)?.AsField();

    public MetadataUsage? GetMethodGlobalByAddress(ulong address)
        => Metadata.MetadataVersion < 27f ? MethodRefsByAddress.GetOrDefault(address) : GetAnyGlobalByAddress(address);

    public Il2CppMethodDefinition? GetMethodDefinitionByGlobalAddress(ulong address)
    {
        var global = GetMethodGlobalByAddress(address);

        if (global?.Type == MetadataUsageType.MethodRef)
            return global.AsGenericMethodRef().BaseMethod;

        return global?.AsMethod();
    }
}
