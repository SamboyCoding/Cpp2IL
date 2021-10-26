using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Metadata
{
    public class Il2CppMetadata : ClassReadingBinaryReader
    {
        //Disable null check as this stuff is reflected.
#pragma warning disable 8618
        public Il2CppGlobalMetadataHeader metadataHeader;
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
        public Il2CppCustomAttributeTypeRange[] attributeTypeRanges;
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
        public Dictionary<uint, SortedDictionary<uint, uint>> metadataUsageDic;

        public long maxMetadataUsages;
        public int[] nestedTypeIndices;
        public Il2CppEventDefinition[] eventDefs;
        public Il2CppGenericContainer[] genericContainers;
        public Il2CppFieldRef[] fieldRefs;
        public Il2CppGenericParameter[] genericParameters;
        public int[] constraintIndices;

        private readonly Dictionary<int, Il2CppFieldDefaultValue> _fieldDefaultValueLookup = new Dictionary<int, Il2CppFieldDefaultValue>();
        private readonly Dictionary<Il2CppFieldDefinition, Il2CppFieldDefaultValue> _fieldDefaultLookupNew = new Dictionary<Il2CppFieldDefinition, Il2CppFieldDefaultValue>();

        public static Il2CppMetadata? ReadFrom(byte[] bytes, int[] unityVer)
        {
            if (BitConverter.ToUInt32(bytes, 0) != 0xFAB11BAF)
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
                if (unityVer[0] == 2021 || (unityVer[0] == 2020 && unityVer[1] >= 3) || (unityVer[0] == 2020 && unityVer[1] == 2 && unityVer[2] >= 4)) actualVersion = 27.1f; //27.1 (2020.2.4) adds adjustorThunks on codegenModules and GenericMethodIndices
                else actualVersion = version; //2020.2 introduces v27
            } else if (version == 24)
            {
                if (unityVer[0] == 2019 && unityVer[1] == 4 && unityVer[2] >= 21) actualVersion = 24.5f;
                //Note should there ever be a case of weird issues here, there *is* actually a 24.4, but it's barely ever used. Only change is AssemblyNameDefinition is missing
                //the hashValueIndex field, which makes the number of assemblies mismatch the number of images.
                //But we don't use AssemblyDefinitions anyway, so... /shrug.
                else if ((unityVer[0] == 2019 && unityVer[1] >= 3) || (unityVer[0] == 2020 && unityVer[1] < 2)) actualVersion = 24.3f; //2019.3 - 2020.1 => 24.3
                else if (unityVer[0] >= 2019) actualVersion = 24.2f; //2019.1 - 2019.2 => 24.2
                else if (unityVer[0] == 2018 && unityVer[1] >= 3) actualVersion = 24.1f; //2018.3 - 2018.4 => 24.1
                else actualVersion = version; //2018.1 - 2018.2 => 24
            }
            else actualVersion = version; //2018.1 - 2018.2 => 24

            LibLogger.InfoNewline($"\tUsing actual IL2CPP Metadata version {actualVersion}");

            LibCpp2IlMain.MetadataVersion = actualVersion;

            return new Il2CppMetadata(new MemoryStream(bytes));
        }

        private Il2CppMetadata(MemoryStream stream) : base(stream)
        {
            metadataHeader = ReadClassAtRawAddr<Il2CppGlobalMetadataHeader>(-1);
            if (metadataHeader.magicNumber != 0xFAB11BAF)
            {
                throw new Exception("ERROR: Magic number mismatch. Expecting " + 0xFAB11BAF + " but got " + metadataHeader.magicNumber);
            }

            if (metadataHeader.version < 24) throw new Exception("ERROR: Invalid metadata version, we only support v24+, this metadata is using v" + metadataHeader.version);

            LibLogger.Verbose("\tReading image definitions...");
            var start = DateTime.Now;
            imageDefinitions = ReadMetadataClassArray<Il2CppImageDefinition>(metadataHeader.imagesOffset, metadataHeader.imagesCount);
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
            VTableMethodIndices = ReadMetadataClassArray<uint>(metadataHeader.vtableMethodsOffset, metadataHeader.vtableMethodsCount);
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
            constraintIndices = ReadMetadataClassArray<int>(metadataHeader.genericParameterConstraintsOffset, metadataHeader.genericParameterConstraintsCount);
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
                attributeTypeRanges = ReadMetadataClassArray<Il2CppCustomAttributeTypeRange>(metadataHeader.attributesInfoOffset, metadataHeader.attributesInfoCount);
                attributeTypes = ReadClassArrayAtRawAddr<int>(metadataHeader.attributeTypesOffset, metadataHeader.attributeTypesCount / 4);
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }
            else
            {
                //Since v29
                LibLogger.Verbose("\tReading Attribute data...");
                start = DateTime.Now;
                
                //Pointer array
                AttributeDataRanges = ReadClassArrayAtRawAddr<Il2CppCustomAttributeDataRange>(metadataHeader.attributeDataRangeOffset, metadataHeader.attributeDataRangeCount / 8).ToList();
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

        private T[] ReadMetadataClassArray<T>(int offset, int length) where T : new()
        {
            return ReadClassArrayAtRawAddr<T>(offset, length / LibCpp2ILUtils.VersionAwareSizeOf(typeof(T), downsize: false));
        }

        private void DecipherMetadataUsage()
        {
            metadataUsageDic = new Dictionary<uint, SortedDictionary<uint, uint>>();
            for (var i = 1u; i <= 6u; i++)
            {
                metadataUsageDic[i] = new SortedDictionary<uint, uint>();
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

            maxMetadataUsages = metadataUsageDic.Max(x => x.Value.Max(y => y.Key)) + 1;
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
            if ((fieldType.attrs & (int) FieldAttributes.HasFieldRVA) != 0)
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
            if (!_cachedStrings.ContainsKey(index))
                _cachedStrings[index] = ReadStringToNull(metadataHeader.stringOffset + index);

            return _cachedStrings[index];
        }

        private ConcurrentDictionary<Il2CppImageDefinition, Il2CppCustomAttributeTypeRange[]> _typeRangesByAssembly = new ();

        public Il2CppCustomAttributeTypeRange? GetCustomAttributeData(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token)
        {
            if (LibCpp2IlMain.MetadataVersion <= 24f)
                return attributeTypeRanges[customAttributeIndex];

            Il2CppCustomAttributeTypeRange[] range;
            lock (_typeRangesByAssembly)
            {
                if (!_typeRangesByAssembly.ContainsKey(imageDef))
                {
                    range = attributeTypeRanges.SubArray(imageDef.customAttributeStart, (int) imageDef.customAttributeCount);
                    _typeRangesByAssembly.TryAdd(imageDef, range);
                }
                else
                {
                    range = _typeRangesByAssembly[imageDef];
                }
            }

            foreach (var r in range)
            {
                if (r.token == token) return r;
            }

            return null;
        }

        public string GetStringLiteralFromIndex(uint index)
        {
            var stringLiteral = stringLiterals[index];
            Position = metadataHeader.stringLiteralDataOffset + stringLiteral.dataIndex;
            return Encoding.UTF8.GetString(ReadBytes((int) stringLiteral.length));
        }
    }
}