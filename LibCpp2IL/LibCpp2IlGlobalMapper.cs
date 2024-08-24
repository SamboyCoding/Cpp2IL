using System.Collections.Generic;
using System.Linq;
using LibCpp2IL.Metadata;

namespace LibCpp2IL;

public static class LibCpp2IlGlobalMapper
{
    internal static List<MetadataUsage> TypeRefs = [];
    internal static List<MetadataUsage> MethodRefs = [];
    internal static List<MetadataUsage> FieldRefs = [];
    internal static List<MetadataUsage> Literals = [];

    internal static Dictionary<ulong, MetadataUsage> TypeRefsByAddress = new();
    internal static Dictionary<ulong, MetadataUsage> MethodRefsByAddress = new();
    internal static Dictionary<ulong, MetadataUsage> FieldRefsByAddress = new();
    internal static Dictionary<ulong, MetadataUsage> LiteralsByAddress = new();

    internal static void Reset()
    {
        TypeRefs.Clear();
        MethodRefs.Clear();
        FieldRefs.Clear();
        Literals.Clear();
        TypeRefsByAddress.Clear();
        MethodRefsByAddress.Clear();
        FieldRefsByAddress.Clear();
        LiteralsByAddress.Clear();
    }

    internal static void MapGlobalIdentifiers(Il2CppMetadata metadata, Il2CppBinary cppAssembly)
    {
        if (LibCpp2IlMain.MetadataVersion < 27f)
            MapGlobalIdentifiersPre27(metadata, cppAssembly);
        else
            MapGlobalIdentifiersPost27(metadata, cppAssembly);
    }

    private static void MapGlobalIdentifiersPost27(Il2CppMetadata metadata, Il2CppBinary cppAssembly)
    {
        //No-op
    }

    private static void MapGlobalIdentifiersPre27(Il2CppMetadata metadata, Il2CppBinary cppAssembly)
    {
        //Type 1 => TypeInfo
        //Type 2 => Il2CppType
        //Type 3 => MethodDef
        //Type 4 => FieldInfo
        //Type 5 => StringLiteral
        //Type 6 => MethodRef

        //Type references

        //We non-null assert here because this function is only called pre-27, when this is guaranteed to be non-null
        TypeRefs = metadata.metadataUsageDic![(uint)MetadataUsageType.TypeInfo]
            .Select(kvp => new MetadataUsage(MetadataUsageType.Type, cppAssembly.GetRawMetadataUsage(kvp.Key), kvp.Value))
            .ToList();

        //More type references
        TypeRefs.AddRange(metadata.metadataUsageDic[(uint)MetadataUsageType.Type]
            .Select(kvp => new MetadataUsage(MetadataUsageType.Type, cppAssembly.GetRawMetadataUsage(kvp.Key), kvp.Value))
        );

        //Method references
        MethodRefs = metadata.metadataUsageDic[(uint)MetadataUsageType.MethodDef]
            .Select(kvp => new MetadataUsage(MetadataUsageType.MethodDef, cppAssembly.GetRawMetadataUsage(kvp.Key), kvp.Value))
            .ToList();

        //Field references
        FieldRefs = metadata.metadataUsageDic[(uint)MetadataUsageType.FieldInfo]
            .Select(kvp => new MetadataUsage(MetadataUsageType.FieldInfo, cppAssembly.GetRawMetadataUsage(kvp.Key), kvp.Value))
            .ToList();

        //Literals
        Literals = metadata.metadataUsageDic[(uint)MetadataUsageType.StringLiteral]
            .Select(kvp => new MetadataUsage(MetadataUsageType.StringLiteral, cppAssembly.GetRawMetadataUsage(kvp.Key), kvp.Value)).ToList();

        //Generic method references
        foreach (var (metadataUsageIdx, methodSpecIdx) in metadata.metadataUsageDic[(uint)MetadataUsageType.MethodRef]) //kIl2CppMetadataUsageMethodRef
        {
            MethodRefs.Add(new MetadataUsage(MetadataUsageType.MethodRef, cppAssembly.GetRawMetadataUsage(metadataUsageIdx), methodSpecIdx));
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

    public static MetadataUsage? CheckForPost27GlobalAt(ulong address)
    {
        if (!LibCpp2IlMain.Binary!.TryMapVirtualAddressToRaw(address, out var raw) || raw >= LibCpp2IlMain.Binary.RawLength)
            return null;

        var encoded = LibCpp2IlMain.Binary.ReadPointerAtVirtualAddress(address);
        var metadataUsage = MetadataUsage.DecodeMetadataUsage(encoded, address);

        if (metadataUsage?.IsValid != true)
            return null;

        return metadataUsage;
    }
}
