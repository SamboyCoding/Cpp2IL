namespace LibCpp2IL.Metadata;

public class Il2CppGlobalMetadataHeader : ReadableClass
{
    public uint magicNumber;
    public int version;
    public int stringLiteralOffset; // string data for managed code
    public int stringLiteralCount;
    public int stringLiteralDataOffset;
    public int stringLiteralDataCount;
    public int stringOffset; // string data for metadata
    public int stringCount;
    public int eventsOffset; // Il2CppEventDefinition
    public int eventsCount;
    public int propertiesOffset; // Il2CppPropertyDefinition
    public int propertiesCount;
    public int methodsOffset; // Il2CppMethodDefinition
    public int methodsCount;
    public int parameterDefaultValuesOffset; // Il2CppParameterDefaultValue
    public int parameterDefaultValuesCount;
    public int fieldDefaultValuesOffset; // Il2CppFieldDefaultValue
    public int fieldDefaultValuesCount;
    public int fieldAndParameterDefaultValueDataOffset; // uint8_t
    public int fieldAndParameterDefaultValueDataCount;
    public int fieldMarshaledSizesOffset; // Il2CppFieldMarshaledSize
    public int fieldMarshaledSizesCount;
    public int parametersOffset; // Il2CppParameterDefinition
    public int parametersCount;
    public int fieldsOffset; // Il2CppFieldDefinition
    public int fieldsCount;
    public int genericParametersOffset; // Il2CppGenericParameter
    public int genericParametersCount;
    public int genericParameterConstraintsOffset; // TypeIndex
    public int genericParameterConstraintsCount;
    public int genericContainersOffset; // Il2CppGenericContainer
    public int genericContainersCount;
    public int nestedTypesOffset; // TypeDefinitionIndex
    public int nestedTypesCount;
    public int interfacesOffset; // TypeIndex
    public int interfacesCount;
    public int vtableMethodsOffset; // EncodedMethodIndex
    public int vtableMethodsCount;
    public int interfaceOffsetsOffset; // Il2CppInterfaceOffsetPair
    public int interfaceOffsetsCount;
    public int typeDefinitionsOffset; // Il2CppTypeDefinition
    public int typeDefinitionsCount;
    [Version(Max = 24.15f)] public int rgctxEntriesOffset; // Il2CppRGCTXDefinition
    [Version(Max = 24.15f)] public int rgctxEntriesCount;
    public int imagesOffset; // Il2CppImageDefinition
    public int imagesCount;
    public int assembliesOffset; // Il2CppAssemblyDefinition
    public int assembliesCount;
    [Version(Max = 24.5f)] public int metadataUsageListsOffset; // Il2CppMetadataUsageList, Removed in v27
    [Version(Max = 24.5f)] public int metadataUsageListsCount; //Removed in v27
    [Version(Max = 24.5f)] public int metadataUsagePairsOffset; // Il2CppMetadataUsagePair, Removed in v27
    [Version(Max = 24.5f)] public int metadataUsagePairsCount; //Removed in v27
    public int fieldRefsOffset; // Il2CppFieldRef
    public int fieldRefsCount;
    public int referencedAssembliesOffset; // int32_t
    public int referencedAssembliesCount;

    //Pre-29 attribute data
    [Version(Max = 27.9f)] public int attributesInfoOffset; // Il2CppCustomAttributeTypeRange
    [Version(Max = 27.9f)] public int attributesInfoCount;
    [Version(Max = 27.9f)] public int attributeTypesOffset; // TypeIndex
    [Version(Max = 27.9f)] public int attributeTypesCount;

    //Post-29 attribute data
    [Version(Min = 27.9f)] public int attributeDataOffset; //uint8_t
    [Version(Min = 27.9f)] public int attributeDataCount;
    [Version(Min = 27.9f)] public int attributeDataRangeOffset; //Il2CppCustomAttributeDataRange
    [Version(Min = 27.9f)] public int attributeDataRangeCount;

    public int unresolvedVirtualCallParameterTypesOffset; // TypeIndex
    public int unresolvedVirtualCallParameterTypesCount;
    public int unresolvedVirtualCallParameterRangesOffset; // Il2CppRange
    public int unresolvedVirtualCallParameterRangesCount;

    [Version(Min = 23)] public int windowsRuntimeTypeNamesOffset; // Il2CppWindowsRuntimeTypeNamePair
    [Version(Min = 23)] public int windowsRuntimeTypeNamesSize;

    [Version(Min = 27)] public int windowsRuntimeStringsOffset; // const char*
    [Version(Min = 27)] public int windowsRuntimeStringsSize;

    [Version(Min = 24)] public int exportedTypeDefinitionsOffset; // TypeDefinitionIndex
    [Version(Min = 24)] public int exportedTypeDefinitionsCount;

    public override void Read(ClassReadingBinaryReader reader)
    {
        magicNumber = reader.ReadUInt32();
        version = reader.ReadInt32();
        stringLiteralOffset = reader.ReadInt32();
        stringLiteralCount = reader.ReadInt32();
        stringLiteralDataOffset = reader.ReadInt32();
        stringLiteralDataCount = reader.ReadInt32();
        stringOffset = reader.ReadInt32();
        stringCount = reader.ReadInt32();
        eventsOffset = reader.ReadInt32();
        eventsCount = reader.ReadInt32();
        propertiesOffset = reader.ReadInt32();
        propertiesCount = reader.ReadInt32();
        methodsOffset = reader.ReadInt32();
        methodsCount = reader.ReadInt32();
        parameterDefaultValuesOffset = reader.ReadInt32();
        parameterDefaultValuesCount = reader.ReadInt32();
        fieldDefaultValuesOffset = reader.ReadInt32();
        fieldDefaultValuesCount = reader.ReadInt32();
        fieldAndParameterDefaultValueDataOffset = reader.ReadInt32();
        fieldAndParameterDefaultValueDataCount = reader.ReadInt32();
        fieldMarshaledSizesOffset = reader.ReadInt32();
        fieldMarshaledSizesCount = reader.ReadInt32();
        parametersOffset = reader.ReadInt32();
        parametersCount = reader.ReadInt32();
        fieldsOffset = reader.ReadInt32();
        fieldsCount = reader.ReadInt32();
        genericParametersOffset = reader.ReadInt32();
        genericParametersCount = reader.ReadInt32();
        genericParameterConstraintsOffset = reader.ReadInt32();
        genericParameterConstraintsCount = reader.ReadInt32();
        genericContainersOffset = reader.ReadInt32();
        genericContainersCount = reader.ReadInt32();
        nestedTypesOffset = reader.ReadInt32();
        nestedTypesCount = reader.ReadInt32();
        interfacesOffset = reader.ReadInt32();
        interfacesCount = reader.ReadInt32();
        vtableMethodsOffset = reader.ReadInt32();
        vtableMethodsCount = reader.ReadInt32();
        interfaceOffsetsOffset = reader.ReadInt32();
        interfaceOffsetsCount = reader.ReadInt32();
        typeDefinitionsOffset = reader.ReadInt32();
        typeDefinitionsCount = reader.ReadInt32();

        if (IsAtMost(24.15f))
        {
            rgctxEntriesOffset = reader.ReadInt32();
            rgctxEntriesCount = reader.ReadInt32();
        }

        imagesOffset = reader.ReadInt32();
        imagesCount = reader.ReadInt32();
        assembliesOffset = reader.ReadInt32();
        assembliesCount = reader.ReadInt32();

        if (IsLessThan(27f))
        {
            metadataUsageListsOffset = reader.ReadInt32();
            metadataUsageListsCount = reader.ReadInt32();
            metadataUsagePairsOffset = reader.ReadInt32();
            metadataUsagePairsCount = reader.ReadInt32();
        }

        fieldRefsOffset = reader.ReadInt32();
        fieldRefsCount = reader.ReadInt32();
        referencedAssembliesOffset = reader.ReadInt32();
        referencedAssembliesCount = reader.ReadInt32();

        if (IsLessThan(29f))
        {
            attributesInfoOffset = reader.ReadInt32();
            attributesInfoCount = reader.ReadInt32();
            attributeTypesOffset = reader.ReadInt32();
            attributeTypesCount = reader.ReadInt32();
        }
        else
        {
            attributeDataOffset = reader.ReadInt32();
            attributeDataCount = reader.ReadInt32();
            attributeDataRangeOffset = reader.ReadInt32();
            attributeDataRangeCount = reader.ReadInt32();
        }

        unresolvedVirtualCallParameterTypesOffset = reader.ReadInt32();
        unresolvedVirtualCallParameterTypesCount = reader.ReadInt32();
        unresolvedVirtualCallParameterRangesOffset = reader.ReadInt32();
        unresolvedVirtualCallParameterRangesCount = reader.ReadInt32();
        windowsRuntimeTypeNamesOffset = reader.ReadInt32();
        windowsRuntimeTypeNamesSize = reader.ReadInt32();

        if (IsAtLeast(27f))
        {
            windowsRuntimeStringsOffset = reader.ReadInt32();
            windowsRuntimeStringsSize = reader.ReadInt32();
        }

        if (IsAtLeast(24f))
        {
            exportedTypeDefinitionsOffset = reader.ReadInt32();
            exportedTypeDefinitionsCount = reader.ReadInt32();
        }
    }
}
