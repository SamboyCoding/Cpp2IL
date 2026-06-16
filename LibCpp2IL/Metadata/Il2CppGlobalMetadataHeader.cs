namespace LibCpp2IL.Metadata;

public class Il2CppGlobalMetadataHeader : ReadableClass
{
    public uint magicNumber;
    public int version;

#nullable disable

    public Il2CppGlobalMetadataSectionHeader stringLiteral; // string data for managed code
    public Il2CppGlobalMetadataSectionHeader stringLiteralData;
    public Il2CppGlobalMetadataSectionHeader @string; // string data for metadata
    public Il2CppGlobalMetadataSectionHeader events; // Il2CppEventDefinition
    public Il2CppGlobalMetadataSectionHeader properties; // Il2CppPropertyDefinition
    public Il2CppGlobalMetadataSectionHeader methods; // Il2CppMethodDefinition
    public Il2CppGlobalMetadataSectionHeader parameterDefaultValues; // Il2CppParameterDefaultValue
    public Il2CppGlobalMetadataSectionHeader fieldDefaultValues; // Il2CppFieldDefaultValue
    public Il2CppGlobalMetadataSectionHeader fieldAndParameterDefaultValueData; // uint8_t
    public Il2CppGlobalMetadataSectionHeader fieldMarshaledSizes; // Il2CppFieldMarshaledSize
    public Il2CppGlobalMetadataSectionHeader parameters; // Il2CppParameterDefinition
    public Il2CppGlobalMetadataSectionHeader fields; // Il2CppFieldDefinition
    public Il2CppGlobalMetadataSectionHeader genericParameters; // Il2CppGenericParameter
    public Il2CppGlobalMetadataSectionHeader genericParameterConstraints; // TypeIndex
    public Il2CppGlobalMetadataSectionHeader genericContainers; // Il2CppGenericContainer
    public Il2CppGlobalMetadataSectionHeader nestedTypes; // TypeDefinitionIndex
    public Il2CppGlobalMetadataSectionHeader interfaces; // TypeIndex
    public Il2CppGlobalMetadataSectionHeader vtableMethods; // EncodedMethodIndex
    public Il2CppGlobalMetadataSectionHeader interfaceOffsets; // Il2CppInterfaceOffsetPair
    public Il2CppGlobalMetadataSectionHeader typeDefinitions; // Il2CppTypeDefinition
    
    [Version(Min = 104f)] public Il2CppGlobalMetadataSectionHeader typeInlineArrays; // Il2CppInlineArrayLength

    [Version(Max = 24.15f)] public Il2CppGlobalMetadataSectionHeader rgctxEntries; // Il2CppRGCTXDefinition

    public Il2CppGlobalMetadataSectionHeader images; // Il2CppImageDefinition
    public Il2CppGlobalMetadataSectionHeader assemblies; // Il2CppAssemblyDefinition

    [Version(Max = 24.5f)] public Il2CppGlobalMetadataSectionHeader metadataUsageLists; // Il2CppMetadataUsageList, Removed in v27
    [Version(Max = 24.5f)] public Il2CppGlobalMetadataSectionHeader metadataUsagePairs; // Il2CppMetadataUsagePair, Removed in v27

    public Il2CppGlobalMetadataSectionHeader fieldRefs; // Il2CppFieldRef
    public Il2CppGlobalMetadataSectionHeader referencedAssemblies; // int32_t

    //Pre-29 attribute data
    [Version(Max = 27.9f)] public Il2CppGlobalMetadataSectionHeader attributesInfo; // Il2CppCustomAttributeTypeRange
    [Version(Max = 27.9f)] public Il2CppGlobalMetadataSectionHeader attributeTypes; // TypeIndex

    //Post-29 attribute data
    [Version(Min = 27.9f)] public Il2CppGlobalMetadataSectionHeader attributeData; //uint8_t
    [Version(Min = 27.9f)] public Il2CppGlobalMetadataSectionHeader attributeDataRange; //Il2CppCustomAttributeDataRange

    public Il2CppGlobalMetadataSectionHeader unresolvedVirtualCallParameterTypes; // TypeIndex
    public Il2CppGlobalMetadataSectionHeader unresolvedVirtualCallParameterRanges; // Il2CppRange

    [Version(Min = 23)] public Il2CppGlobalMetadataSectionHeader windowsRuntimeTypeNames; // Il2CppWindowsRuntimeTypeNamePair

    [Version(Min = 27)] public Il2CppGlobalMetadataSectionHeader windowsRuntimeStrings; // const char*

    [Version(Min = 24)] public Il2CppGlobalMetadataSectionHeader exportedTypeDefinitions; // TypeDefinitionIndex

#nullable restore

    public override void Read(ClassReadingBinaryReader reader)
    {
        magicNumber = reader.ReadUInt32();
        version = reader.ReadInt32();

        stringLiteral = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        stringLiteralData = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        @string = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        events = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        properties = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        methods = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        parameterDefaultValues = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        fieldDefaultValues = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        fieldAndParameterDefaultValueData = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        fieldMarshaledSizes = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        parameters = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        fields = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        genericParameters = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        genericParameterConstraints = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        genericContainers = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        nestedTypes = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        interfaces = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        vtableMethods = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        interfaceOffsets = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        typeDefinitions = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        
        if (IsAtLeast(104f))
            typeInlineArrays = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        if (IsAtMost(24.15f))
            rgctxEntries = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        images = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        assemblies = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        if (IsLessThan(27f))
        {
            metadataUsageLists = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
            metadataUsagePairs = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        }

        fieldRefs = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        referencedAssemblies = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        if (IsLessThan(29f))
        {
            attributesInfo = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
            attributeTypes = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        }
        else
        {
            attributeData = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
            attributeDataRange = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        }

        unresolvedVirtualCallParameterTypes = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        unresolvedVirtualCallParameterRanges = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        windowsRuntimeTypeNames = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        if (IsAtLeast(27f))
            windowsRuntimeStrings = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        if (IsAtLeast(24f))
            exportedTypeDefinitions = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
    }
}
