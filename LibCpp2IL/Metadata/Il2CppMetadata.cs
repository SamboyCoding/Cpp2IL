using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AssetRipper.VersionUtilities;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Metadata
{
    public class Il2CppMetadata : ClassReadingBinaryReader
    {
        //Disable null check as this stuff is reflected.
#pragma warning disable 8618
        public Il2CppGlobalMetadataHeader metadataHeader;
        public Il2CppAssemblyDefinition[] AssemblyDefinitions;
        public Il2CppImageDefinition[] imageDefinitions;
        public Il2CppTypeDefinition[] typeDefs;
        internal Il2CppInterfaceOffset[] interfaceOffsets;
        public uint[] VTableMethodIndices;
        public Il2CppMethodDefinition[] methodDefs;
        public Il2CppParameterDefinition[] parameterDefs;
        public Il2CppFieldDefinition[] fieldDefs;
        private Il2CppFieldDefaultValue[] fieldDefaultValues;
        private Il2CppParameterDefaultValue[] parameterDefaultValues;
        public Il2CppPropertyDefinition[] propertyDefs;
        public List<Il2CppCustomAttributeTypeRange> attributeTypeRanges;
        private Il2CppStringLiteral[] stringLiterals;
        public Il2CppMetadataUsageList[] metadataUsageLists;
        private Il2CppMetadataUsagePair[] metadataUsagePairs;
        public Il2CppRGCTXDefinition[] RgctxDefinitions; //Moved to binary in v24.2

        //Pre-29
        public int[] attributeTypes;
        public int[] interfaceIndices;

        //Post-29
        public List<Il2CppCustomAttributeDataRange> AttributeDataRanges;

        //Moved to binary in v27.
        public Dictionary<uint, SortedDictionary<uint, uint>>? metadataUsageDic;

        public int[] nestedTypeIndices;
        public Il2CppEventDefinition[] eventDefs;
        public Il2CppGenericContainer[] genericContainers;
        public Il2CppFieldRef[] fieldRefs;
        public Il2CppGenericParameter[] genericParameters;
        public int[] constraintIndices;

        public int[] referencedAssemblies;

        private readonly Dictionary<int, Il2CppFieldDefaultValue> _fieldDefaultValueLookup = new Dictionary<int, Il2CppFieldDefaultValue>();
        private readonly Dictionary<Il2CppFieldDefinition, Il2CppFieldDefaultValue> _fieldDefaultLookupNew = new Dictionary<Il2CppFieldDefinition, Il2CppFieldDefaultValue>();

        public static bool HasMetadataHeader(byte[] bytes) => bytes.Length >= 4 && BitConverter.ToUInt32(bytes, 0) == 0xFAB11BAF;

        public static Il2CppMetadata? ReadFrom(byte[] bytes, UnityVersion unityVersion)
        {
            if (!HasMetadataHeader(bytes))
            {
                //Magic number is wrong
                throw new FormatException("Invalid or corrupt metadata (magic number check failed)");
            }

            var version = BitConverter.ToInt32(bytes, 4);
            if (version is < 24 or > 29)
            {
                throw new FormatException("Unsupported metadata version found! We support 24-29, got " + version);
            }

            LibLogger.VerboseNewline($"\tIL2CPP Metadata Declares its version as {version}");

            float actualVersion;
            if (version == 27)
            {
                if (unityVersion.IsGreaterEqual(2021, 1))
                    actualVersion = 27.2f; //2021.1 and up is v27.2, which just changes Il2CppType to have one new bit
                else if (unityVersion.IsGreaterEqual(2020, 2, 4))
                    actualVersion = 27.1f; //2020.2.4 and above is v27.1
                else
                    actualVersion = version; //2020.2 and above is v27
            }
            else if (version == 24)
            {
                if (unityVersion.IsGreaterEqual(2020, 1, 11))
                    actualVersion = 24.4f; //2020.1.11-17 were released prior to 2019.4.21, so are still on 24.4
                else if (unityVersion.IsGreaterEqual(2020))
                    actualVersion = 24.3f; //2020.1.0-10 were released prior to to 2019.4.15, so are still on 24.3
                else if (unityVersion.IsGreaterEqual(2019, 4, 21))
                    actualVersion = 24.5f; //2019.4.21 introduces v24.5
                else if (unityVersion.IsGreaterEqual(2019, 4, 15))
                    actualVersion = 24.4f; //2019.4.15 introduces v24.4
                else if (unityVersion.IsGreaterEqual(2019, 3, 7))
                    actualVersion = 24.3f; //2019.3.7 introduces v24.3
                else if (unityVersion.IsGreaterEqual(2019))
                    actualVersion = 24.2f; //2019.1.0 introduces v24.2
                else if (unityVersion.IsGreaterEqual(2018, 4, 34))
                    actualVersion = 24.15f; //2018.4.34 made a tiny little change which just removes HashValueIndex from AssemblyNameDefinition
                else if (unityVersion.IsGreaterEqual(2018, 3))
                    actualVersion = 24.1f; //2018.3.0 introduces v24.1
                else
                    actualVersion = version; //2017.1.0 was the first v24 version
            }
            else actualVersion = version; //Covers v29

            LibLogger.InfoNewline($"\tUsing actual IL2CPP Metadata version {actualVersion}");

            LibCpp2IlMain.MetadataVersion = actualVersion;

            return new Il2CppMetadata(new MemoryStream(bytes));
        }

        private Il2CppMetadata(MemoryStream stream) : base(stream)
        {
            metadataHeader = ReadReadable<Il2CppGlobalMetadataHeader>();
            if (metadataHeader.magicNumber != 0xFAB11BAF)
            {
                throw new Exception("ERROR: Magic number mismatch. Expecting " + 0xFAB11BAF + " but got " + metadataHeader.magicNumber);
            }

            if (metadataHeader.version < 24) throw new Exception("ERROR: Invalid metadata version, we only support v24+, this metadata is using v" + metadataHeader.version);

            LibLogger.Verbose("\tReading image definitions...");
            var start = DateTime.Now;
            imageDefinitions = ReadMetadataClassArray<Il2CppImageDefinition>(metadataHeader.imagesOffset, metadataHeader.imagesCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading assembly definitions...");
            start = DateTime.Now;
            AssemblyDefinitions = ReadMetadataClassArray<Il2CppAssemblyDefinition>(metadataHeader.assembliesOffset, metadataHeader.assembliesCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading type definitions...");
            start = DateTime.Now;
            typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(metadataHeader.typeDefinitionsOffset, metadataHeader.typeDefinitionsCount);
            LibLogger.VerboseNewline($"{typeDefs.Length} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading interface offsets...");
            start = DateTime.Now;
            interfaceOffsets = ReadMetadataClassArray<Il2CppInterfaceOffset>(metadataHeader.interfaceOffsetsOffset, metadataHeader.interfaceOffsetsCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading vtable indices...");
            start = DateTime.Now;
            VTableMethodIndices = ReadClassArrayAtRawAddr<uint>(metadataHeader.vtableMethodsOffset, metadataHeader.vtableMethodsCount / sizeof(uint));
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading method definitions...");
            start = DateTime.Now;
            methodDefs = ReadMetadataClassArray<Il2CppMethodDefinition>(metadataHeader.methodsOffset, metadataHeader.methodsCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading method parameter definitions...");
            start = DateTime.Now;
            parameterDefs = ReadMetadataClassArray<Il2CppParameterDefinition>(metadataHeader.parametersOffset, metadataHeader.parametersCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading field definitions...");
            start = DateTime.Now;
            fieldDefs = ReadMetadataClassArray<Il2CppFieldDefinition>(metadataHeader.fieldsOffset, metadataHeader.fieldsCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading default field values...");
            start = DateTime.Now;
            fieldDefaultValues = ReadMetadataClassArray<Il2CppFieldDefaultValue>(metadataHeader.fieldDefaultValuesOffset, metadataHeader.fieldDefaultValuesCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading default parameter values...");
            start = DateTime.Now;
            parameterDefaultValues = ReadMetadataClassArray<Il2CppParameterDefaultValue>(metadataHeader.parameterDefaultValuesOffset, metadataHeader.parameterDefaultValuesCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading property definitions...");
            start = DateTime.Now;
            propertyDefs = ReadMetadataClassArray<Il2CppPropertyDefinition>(metadataHeader.propertiesOffset, metadataHeader.propertiesCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading interface definitions...");
            start = DateTime.Now;
            interfaceIndices = ReadClassArrayAtRawAddr<int>(metadataHeader.interfacesOffset, metadataHeader.interfacesCount / 4);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading nested type definitions...");
            start = DateTime.Now;
            nestedTypeIndices = ReadClassArrayAtRawAddr<int>(metadataHeader.nestedTypesOffset, metadataHeader.nestedTypesCount / 4);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading event definitions...");
            start = DateTime.Now;
            eventDefs = ReadMetadataClassArray<Il2CppEventDefinition>(metadataHeader.eventsOffset, metadataHeader.eventsCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading generic container definitions...");
            start = DateTime.Now;
            genericContainers = ReadMetadataClassArray<Il2CppGenericContainer>(metadataHeader.genericContainersOffset, metadataHeader.genericContainersCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading generic parameter definitions...");
            start = DateTime.Now;
            genericParameters = ReadMetadataClassArray<Il2CppGenericParameter>(metadataHeader.genericParametersOffset, metadataHeader.genericParametersCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading generic parameter constraint indices...");
            start = DateTime.Now;
            constraintIndices = ReadClassArrayAtRawAddr<int>(metadataHeader.genericParameterConstraintsOffset, metadataHeader.genericParameterConstraintsCount / sizeof(int));
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading referenced assemblies...");
            start = DateTime.Now;
            referencedAssemblies = ReadClassArrayAtRawAddr<int>(metadataHeader.referencedAssembliesOffset, metadataHeader.referencedAssembliesCount / sizeof(int));
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            //v17+ fields
            LibLogger.Verbose("\tReading string definitions...");
            start = DateTime.Now;
            stringLiterals = ReadMetadataClassArray<Il2CppStringLiteral>(metadataHeader.stringLiteralOffset, metadataHeader.stringLiteralCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            if (LibCpp2IlMain.MetadataVersion < 24.2f)
            {
                LibLogger.Verbose("\tReading RGCTX data...");
                start = DateTime.Now;

                RgctxDefinitions = ReadMetadataClassArray<Il2CppRGCTXDefinition>(metadataHeader.rgctxEntriesOffset, metadataHeader.rgctxEntriesCount);

                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            //Removed in v27 (2020.2) and also 24.5 (2019.4.21)
            if (LibCpp2IlMain.MetadataVersion < 27f)
            {
                LibLogger.Verbose("\tReading usage data...");
                start = DateTime.Now;
                metadataUsageLists = ReadMetadataClassArray<Il2CppMetadataUsageList>(metadataHeader.metadataUsageListsOffset, metadataHeader.metadataUsageListsCount);
                metadataUsagePairs = ReadMetadataClassArray<Il2CppMetadataUsagePair>(metadataHeader.metadataUsagePairsOffset, metadataHeader.metadataUsagePairsCount);

                DecipherMetadataUsage();
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            LibLogger.Verbose("\tReading field references...");
            start = DateTime.Now;
            fieldRefs = ReadMetadataClassArray<Il2CppFieldRef>(metadataHeader.fieldRefsOffset, metadataHeader.fieldRefsCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            //v21+ fields

            if (LibCpp2IlMain.MetadataVersion < 29)
            {
                //Removed in v29
                LibLogger.Verbose("\tReading attribute types...");
                start = DateTime.Now;
                attributeTypeRanges = ReadMetadataClassArray<Il2CppCustomAttributeTypeRange>(metadataHeader.attributesInfoOffset, metadataHeader.attributesInfoCount).ToList();
                attributeTypes = ReadClassArrayAtRawAddr<int>(metadataHeader.attributeTypesOffset, metadataHeader.attributeTypesCount / 4);
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }
            else
            {
                //Since v29
                LibLogger.Verbose("\tReading Attribute data...");
                start = DateTime.Now;

                //Pointer array
                AttributeDataRanges = ReadReadableArrayAtRawAddr<Il2CppCustomAttributeDataRange>(metadataHeader.attributeDataRangeOffset, metadataHeader.attributeDataRangeCount / 8).ToList();
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            LibLogger.Verbose("\tBuilding Lookup Table for field defaults...");
            start = DateTime.Now;
            foreach (var il2CppFieldDefaultValue in fieldDefaultValues)
            {
                _fieldDefaultValueLookup[il2CppFieldDefaultValue.fieldIndex] = il2CppFieldDefaultValue;
                _fieldDefaultLookupNew[fieldDefs[il2CppFieldDefaultValue.fieldIndex]] = il2CppFieldDefaultValue;
            }

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }
#pragma warning restore 8618

        private T[] ReadMetadataClassArray<T>(int offset, int length) where T : ReadableClass, new()
        {
            return ReadReadableArrayAtRawAddr<T>(offset, length / LibCpp2ILUtils.VersionAwareSizeOf(typeof(T), downsize: false));
        }

        private void DecipherMetadataUsage()
        {
            metadataUsageDic = new();
            for (var i = 1u; i <= 6u; i++)
            {
                metadataUsageDic[i] = new();
            }

            foreach (var metadataUsageList in metadataUsageLists)
            {
                for (var i = 0; i < metadataUsageList.count; i++)
                {
                    var offset = metadataUsageList.start + i;
                    var metadataUsagePair = metadataUsagePairs[offset];
                    var usage = GetEncodedIndexType(metadataUsagePair.encodedSourceIndex);
                    var decodedIndex = GetDecodedMethodIndex(metadataUsagePair.encodedSourceIndex);
                    metadataUsageDic[usage][metadataUsagePair.destinationIndex] = decodedIndex;
                }
            }
        }

        public uint GetMaxMetadataUsages()
        {
            if (metadataUsageDic == null)
                //V27+
                return 0;
            
            return metadataUsageDic.Max(x => x.Value.Max(y => y.Key)) + 1;
        }

        private uint GetEncodedIndexType(uint index)
        {
            return (index & 0xE0000000) >> 29;
        }

        private uint GetDecodedMethodIndex(uint index)
        {
            return index & 0x1FFFFFFFU;
        }

        //Getters for human readability
        public Il2CppFieldDefaultValue? GetFieldDefaultValueFromIndex(int index)
        {
            return _fieldDefaultValueLookup.GetValueOrDefault(index);
        }

        public Il2CppFieldDefaultValue? GetFieldDefaultValue(Il2CppFieldDefinition field)
        {
            return _fieldDefaultLookupNew.GetValueOrDefault(field);
        }

        public (int ptr, int type) GetFieldDefaultValue(int fieldIdx)
        {
            var fieldDef = fieldDefs[fieldIdx];
            var fieldType = LibCpp2IlMain.Binary!.GetType(fieldDef.typeIndex);
            if ((fieldType.Attrs & (int) FieldAttributes.HasFieldRVA) != 0)
            {
                var fieldDefault = GetFieldDefaultValueFromIndex(fieldIdx);

                if (fieldDefault == null)
                    return (-1, -1);

                return (ptr: fieldDefault.dataIndex, type: fieldDefault.typeIndex);
            }

            return (-1, -1);
        }

        public Il2CppParameterDefaultValue? GetParameterDefaultValueFromIndex(int index)
        {
            return parameterDefaultValues.FirstOrDefault(x => x.parameterIndex == index);
        }

        public int GetDefaultValueFromIndex(int index)
        {
            return metadataHeader.fieldAndParameterDefaultValueDataOffset + index;
        }

        private ConcurrentDictionary<int, string> _cachedStrings = new ConcurrentDictionary<int, string>();

        public string GetStringFromIndex(int index)
        {
            GetLockOrThrow();
            try
            {
                return ReadStringFromIndexNoReadLock(index);
            }
            finally
            {
                ReleaseLock();
            }
        }

        internal string ReadStringFromIndexNoReadLock(int index)
        {
            if (!_cachedStrings.ContainsKey(index))
                _cachedStrings[index] = ReadStringToNullNoLock(metadataHeader.stringOffset + index);
            return _cachedStrings[index];
        }

        public Il2CppCustomAttributeTypeRange? GetCustomAttributeData(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token, out int idx)
        {
            idx = -1;

            if (LibCpp2IlMain.MetadataVersion <= 24f)
                return attributeTypeRanges[customAttributeIndex];

            var target = new Il2CppCustomAttributeTypeRange {token = token};

            if (imageDef.customAttributeStart < 0)
                throw new("Image has customAttributeStart < 0");
            if (imageDef.customAttributeStart + imageDef.customAttributeCount > attributeTypeRanges.Count)
                throw new($"Image has customAttributeStart + customAttributeCount > attributeTypeRanges.Count ({imageDef.customAttributeStart + imageDef.customAttributeCount} > {attributeTypeRanges.Count})");

            idx = attributeTypeRanges.BinarySearch(imageDef.customAttributeStart, (int) imageDef.customAttributeCount, target, new TokenComparer());

            return idx < 0 ? null : attributeTypeRanges[idx];
        }

        public string GetStringLiteralFromIndex(uint index)
        {
            var stringLiteral = stringLiterals[index];
            
            return Encoding.UTF8.GetString(ReadByteArrayAtRawAddress(metadataHeader.stringLiteralDataOffset + stringLiteral.dataIndex, (int) stringLiteral.length));
        }
    }
}