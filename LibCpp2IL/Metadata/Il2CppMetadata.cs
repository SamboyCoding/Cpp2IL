using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LibCpp2IL.Metadata
{
    public class Il2CppMetadata : ClassReadingBinaryReader
    {
        //Disable null check as this stuff is reflected.
#pragma warning disable 8618
        private Il2CppGlobalMetadataHeader metadataHeader;
        public Il2CppImageDefinition[] imageDefinitions;
        public Il2CppTypeDefinition[] typeDefs;
        internal Il2CppInterfaceOffset[] interfaceOffsets;
        public Il2CppMethodDefinition[] methodDefs;
        public Il2CppParameterDefinition[] parameterDefs;
        public Il2CppFieldDefinition[] fieldDefs;
        private Il2CppFieldDefaultValue[] fieldDefaultValues;
        private Il2CppParameterDefaultValue[] parameterDefaultValues;
        public Il2CppPropertyDefinition[] propertyDefs;
        public Il2CppCustomAttributeTypeRange[] attributeTypeRanges;
        private Il2CppStringLiteral[] stringLiterals;
        private Il2CppMetadataUsageList[] metadataUsageLists;
        private Il2CppMetadataUsagePair[] metadataUsagePairs;
        public int[] attributeTypes;
        public int[] interfaceIndices;
        
        //Moved to binary in v27.
        public Dictionary<uint, SortedDictionary<uint, uint>> metadataUsageDic;
        
        public long maxMetadataUsages;
        public int[] nestedTypeIndices;
        public Il2CppEventDefinition[] eventDefs;
        public Il2CppGenericContainer[] genericContainers;
        public Il2CppFieldRef[] fieldRefs;
        public Il2CppGenericParameter[] genericParameters;

        private readonly Dictionary<int, Il2CppFieldDefaultValue> _fieldDefaultValueLookup = new Dictionary<int, Il2CppFieldDefaultValue>();

        public static Il2CppMetadata? ReadFrom(byte[] bytes, int[] unityVer)
        {
            if (BitConverter.ToUInt32(bytes, 0) != 0xFAB11BAF)
            {
                //Magic number is wrong
                throw new FormatException("Invalid or corrupt metadata (magic number check failed)");
            }

            var version = BitConverter.ToInt32(bytes, 4);
            if (version < 24)
            {
                throw new FormatException("Unexpected non-unity metadata version found! Expected 24+, got " + version);
            }

            float actualVersion;
            if (unityVer[0] == 2021 || (unityVer[0] == 2020 && unityVer[1] == 2 && unityVer[2] >= 4)) actualVersion = 27.1f; //27.1 adds adjustorThunks on codegenModules and GenericMethodIndices
            else if (unityVer[0] == 2020 && unityVer[1] >= 2) actualVersion = 27; //2020.2 introduces v27
            //Note should there ever be a case of weird issues here, there *is* actually a 24.4, but it's barely ever used. Only change is AssemblyNameDefinition is missing
            //the hashValueIndex field, which makes the number of assemblies mismatch the number of images.
            //But we don't use AssemblyDefinitions anyway, so... /shrug.
            else if ((unityVer[0] == 2019 && unityVer[1] >= 3) || (unityVer[0] == 2020 && unityVer[1] < 2)) actualVersion = 24.3f; //2019.3 - 2020.1 => 24.3
            else if (unityVer[0] >= 2019) actualVersion = 24.2f; //2019.1 - 2019.2 => 24.2
            else if (unityVer[0] == 2018 && unityVer[1] >= 3) actualVersion = 24.1f; //2018.3 - 2018.4 => 24.1
            else actualVersion = version; //2018.1 - 2018.2 => 24

            Console.WriteLine($"Using IL2CPP Metadata version {actualVersion}");

            LibCpp2IlMain.MetadataVersion = actualVersion;

            return new Il2CppMetadata(new MemoryStream(bytes));
        }

        private Il2CppMetadata(MemoryStream stream) : base(stream)
        {
            metadataHeader = ReadClass<Il2CppGlobalMetadataHeader>(-1);
            if (metadataHeader.magicNumber != 0xFAB11BAF)
            {
                throw new Exception("ERROR: Magic number mismatch. Expecting " + 0xFAB11BAF + " but got " + metadataHeader.magicNumber);
            }

            if (metadataHeader.version < 24) throw new Exception("ERROR: Invalid metadata version, we only support v24+, this metadata is using v" + metadataHeader.version);

            Console.Write("\tReading image definitions...");
            var start = DateTime.Now;
            imageDefinitions = ReadMetadataClassArray<Il2CppImageDefinition>(metadataHeader.imagesOffset, metadataHeader.imagesCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading type definitions...");
            start = DateTime.Now;
            typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(metadataHeader.typeDefinitionsOffset, metadataHeader.typeDefinitionsCount);
            Console.WriteLine($"{typeDefs.Length} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading interface offsets...");
            start = DateTime.Now;
            interfaceOffsets = ReadMetadataClassArray<Il2CppInterfaceOffset>(metadataHeader.interfaceOffsetsOffset, metadataHeader.interfaceOffsetsCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading method definitions...");
            start = DateTime.Now;
            methodDefs = ReadMetadataClassArray<Il2CppMethodDefinition>(metadataHeader.methodsOffset, metadataHeader.methodsCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading method parameter definitions...");
            start = DateTime.Now;
            parameterDefs = ReadMetadataClassArray<Il2CppParameterDefinition>(metadataHeader.parametersOffset, metadataHeader.parametersCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading field definitions...");
            start = DateTime.Now;
            fieldDefs = ReadMetadataClassArray<Il2CppFieldDefinition>(metadataHeader.fieldsOffset, metadataHeader.fieldsCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading default field values...");
            start = DateTime.Now;
            fieldDefaultValues = ReadMetadataClassArray<Il2CppFieldDefaultValue>(metadataHeader.fieldDefaultValuesOffset, metadataHeader.fieldDefaultValuesCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading default parameter values...");
            start = DateTime.Now;
            parameterDefaultValues = ReadMetadataClassArray<Il2CppParameterDefaultValue>(metadataHeader.parameterDefaultValuesOffset, metadataHeader.parameterDefaultValuesCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading property definitions...");
            start = DateTime.Now;
            propertyDefs = ReadMetadataClassArray<Il2CppPropertyDefinition>(metadataHeader.propertiesOffset, metadataHeader.propertiesCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading interface definitions...");
            start = DateTime.Now;
            interfaceIndices = ReadClassArray<int>(metadataHeader.interfacesOffset, metadataHeader.interfacesCount / 4);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading nested type definitions...");
            start = DateTime.Now;
            nestedTypeIndices = ReadClassArray<int>(metadataHeader.nestedTypesOffset, metadataHeader.nestedTypesCount / 4);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading event definitions...");
            start = DateTime.Now;
            eventDefs = ReadMetadataClassArray<Il2CppEventDefinition>(metadataHeader.eventsOffset, metadataHeader.eventsCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading generic container definitions...");
            start = DateTime.Now;
            genericContainers = ReadMetadataClassArray<Il2CppGenericContainer>(metadataHeader.genericContainersOffset, metadataHeader.genericContainersCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading generic parameter definitions...");
            start = DateTime.Now;
            genericParameters = ReadMetadataClassArray<Il2CppGenericParameter>(metadataHeader.genericParametersOffset, metadataHeader.genericParametersCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            //v17+ fields
            Console.Write("\tReading string definitions...");
            start = DateTime.Now;
            stringLiterals = ReadMetadataClassArray<Il2CppStringLiteral>(metadataHeader.stringLiteralOffset, metadataHeader.stringLiteralCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            //Removed in v27 (2020.2)
            if (LibCpp2IlMain.MetadataVersion < 27f)
            {
                Console.Write("\tReading usage data...");
                start = DateTime.Now;
                metadataUsageLists = ReadMetadataClassArray<Il2CppMetadataUsageList>(metadataHeader.metadataUsageListsOffset, metadataHeader.metadataUsageListsCount);
                metadataUsagePairs = ReadMetadataClassArray<Il2CppMetadataUsagePair>(metadataHeader.metadataUsagePairsOffset, metadataHeader.metadataUsagePairsCount);

                DecipherMetadataUsage();
                Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            Console.Write("\tReading field references...");
            start = DateTime.Now;
            fieldRefs = ReadMetadataClassArray<Il2CppFieldRef>(metadataHeader.fieldRefsOffset, metadataHeader.fieldRefsCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            //v21+ fields
            Console.Write("\tReading attribute types...");
            start = DateTime.Now;
            attributeTypeRanges = ReadMetadataClassArray<Il2CppCustomAttributeTypeRange>(metadataHeader.attributesInfoOffset, metadataHeader.attributesInfoCount);
            attributeTypes = ReadClassArray<int>(metadataHeader.attributeTypesOffset, metadataHeader.attributeTypesCount / 4);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            
            Console.Write("\tBuilding Lookup Table for field defaults...");
            start = DateTime.Now;
            foreach (var il2CppFieldDefaultValue in fieldDefaultValues)
            {
                _fieldDefaultValueLookup[il2CppFieldDefaultValue.fieldIndex] = il2CppFieldDefaultValue;
            }
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }
#pragma warning restore 8618

        private T[] ReadMetadataClassArray<T>(int offset, int length) where T : new()
        {
            return ReadClassArray<T>(offset, length / LibCpp2ILUtils.VersionAwareSizeOf(typeof(T)));
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
        public Il2CppFieldDefaultValue GetFieldDefaultValueFromIndex(int index)
        {
            return _fieldDefaultValueLookup.GetValueOrDefault(index);
        }

        public Il2CppParameterDefaultValue GetParameterDefaultValueFromIndex(int index)
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
            if(!_cachedStrings.ContainsKey(index))
                _cachedStrings[index] = ReadStringToNull(metadataHeader.stringOffset + index);
            
            return _cachedStrings[index];
        }

        private Dictionary<Il2CppImageDefinition, Il2CppCustomAttributeTypeRange[]> _typeRangesByAssembly = new Dictionary<Il2CppImageDefinition, Il2CppCustomAttributeTypeRange[]>();
        public Il2CppCustomAttributeTypeRange? GetCustomAttributeIndex(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token)
        {
            if (LibCpp2IlMain.MetadataVersion <= 24f) 
                return attributeTypeRanges[customAttributeIndex];
            
            if (!_typeRangesByAssembly.ContainsKey(imageDef))
                _typeRangesByAssembly[imageDef] = attributeTypeRanges.SubArray(imageDef.customAttributeStart, (int) imageDef.customAttributeCount);
                
            foreach (var r in _typeRangesByAssembly[imageDef])
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