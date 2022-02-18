namespace LibCpp2IL.Metadata
{
    public class Il2CppGlobalMetadataHeader
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
        [Version(Max=24.5f)] public int metadataUsageListsOffset; // Il2CppMetadataUsageList, Removed in v27
        [Version(Max=24.5f)] public int metadataUsageListsCount; //Removed in v27
        [Version(Max=24.5f)] public int metadataUsagePairsOffset; // Il2CppMetadataUsagePair, Removed in v27
        [Version(Max=24.5f)] public int metadataUsagePairsCount; //Removed in v27
        public int fieldRefsOffset; // Il2CppFieldRef
        public int fieldRefsCount;
        public int referencedAssembliesOffset; // int32_t
        public int referencedAssembliesCount;
        
        //Pre-29 attribute data
        [Version(Max=27.1f)]public int attributesInfoOffset; // Il2CppCustomAttributeTypeRange
        [Version(Max=27.1f)]public int attributesInfoCount;
        [Version(Max=27.1f)] public int attributeTypesOffset; // TypeIndex
        [Version(Max=27.1f)] public int attributeTypesCount;
        
        //Post-29 attribute data
        [Version(Min = 27.1f)] public int attributeDataOffset; //uint8_t
        [Version(Min = 27.1f)] public int attributeDataCount;
        [Version(Min = 27.1f)] public int attributeDataRangeOffset; //Il2CppCustomAttributeDataRange
        [Version(Min = 27.1f)] public int attributeDataRangeCount; 
        public int unresolvedVirtualCallParameterTypesOffset; // TypeIndex
        public int unresolvedVirtualCallParameterTypesCount;
        public int unresolvedVirtualCallParameterRangesOffset; // Il2CppRange
        public int unresolvedVirtualCallParameterRangesCount;
        public int windowsRuntimeTypeNamesOffset; // Il2CppWindowsRuntimeTypeNamePair
        public int windowsRuntimeTypeNamesSize;
        
        [Version(Min = 27)]
        public int windowsRuntimeStringsOffset; // const char*
        [Version(Min = 27)]
        public int windowsRuntimeStringsSize;
        
        public int exportedTypeDefinitionsOffset; // TypeDefinitionIndex
        public int exportedTypeDefinitionsCount;
    }
}