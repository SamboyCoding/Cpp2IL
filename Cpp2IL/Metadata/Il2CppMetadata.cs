using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Cpp2IL.Metadata
{
    public class Il2CppMetadata : ClassReadingBinaryReader
    {
        private Il2CppGlobalMetadataHeader metadataHeader;
        public Il2CppAssemblyDefinition[] assemblyDefinitions;
        public Il2CppTypeDefinition[] typeDefs;
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
        public Dictionary<uint, SortedDictionary<uint, uint>> metadataUsageDic;
        public long maxMetadataUsages;
        public int[] nestedTypeIndices;
        public Il2CppEventDefinition[] eventDefs;
        public Il2CppGenericContainer[] genericContainers;
        public Il2CppFieldRef[] fieldRefs;
        public Il2CppGenericParameter[] genericParameters;
        
        public static Il2CppMetadata? ReadFrom(string path, int[] unityVer)
        {
            var bytes = File.ReadAllBytes(path);
            if (BitConverter.ToUInt32(bytes, 0) != 0xFAB11BAF)
            {
                //Magic number is wrong
                Console.WriteLine("Error: Invalid or corrupt metadata (magic number check failed): " + path);
                return null;
            }

            var version = BitConverter.ToInt32(bytes, 4);
            if (version != 24)
            {
                Console.WriteLine("Unexpected non-unity metadata version found! Expected 24, got " + version);
                return null;
            }

            float actualVersion;
            if (unityVer[0] >= 2019) actualVersion = 24.2f;
            else if (unityVer[0] == 2018 && unityVer[1] >= 3) actualVersion = 24.1f;
            else actualVersion = version;
            
            Console.WriteLine($"Using IL2CPP Metadata version {actualVersion}");

            Program.MetadataVersion = actualVersion;
            
            return new Il2CppMetadata(new MemoryStream(bytes));
        }

        private Il2CppMetadata(Stream stream) : base(stream)
        {
            metadataHeader = ReadClass<Il2CppGlobalMetadataHeader>(-1);
            if (metadataHeader.magicNumber != 0xFAB11BAF)
            {
                throw new Exception("ERROR: Magic number mismatch. Expecting " + 0xFAB11BAF + " but got " + metadataHeader.magicNumber);
            }
            
            if(metadataHeader.version != 24) throw new Exception("ERROR: Invalid metadata version, unity only uses 24, we got " + metadataHeader.version);

            Console.Write("\tReading image definitions...");
            var start = DateTime.Now;
            assemblyDefinitions = ReadMetadataClassArray<Il2CppAssemblyDefinition>(metadataHeader.imagesOffset, metadataHeader.imagesCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            
            Console.Write("\tReading type definitions...");
            start = DateTime.Now;
            typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(metadataHeader.typeDefinitionsOffset, metadataHeader.typeDefinitionsCount);
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
            
            Console.Write("\tReading usage data...");
            start = DateTime.Now;
            metadataUsageLists = ReadMetadataClassArray<Il2CppMetadataUsageList>(metadataHeader.metadataUsageListsOffset, metadataHeader.metadataUsageListsCount);
            metadataUsagePairs = ReadMetadataClassArray<Il2CppMetadataUsagePair>(metadataHeader.metadataUsagePairsOffset, metadataHeader.metadataUsagePairsCount);

            DecipherMetadataUsage();
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

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
        }
        
        private T[] ReadMetadataClassArray<T>(int offset, int length) where T : new()
        {
            return ReadClassArray<T>(offset, length / VersionAwareSizeOf(typeof(T)));
        }
        
        private static int VersionAwareSizeOf(Type type)
        {
            var size = 0;
            foreach (var i in type.GetFields())
            {
                var attr = (VersionAttribute)Attribute.GetCustomAttribute(i, typeof(VersionAttribute));
                if (attr != null)
                {
                    if (Program.MetadataVersion < attr.Min || Program.MetadataVersion > attr.Max)
                        continue;
                }
                switch (i.FieldType.Name)
                {
                    case "Int32":
                    case "UInt32":
                        size += 4;
                        break;
                    case "Int16":
                    case "UInt16":
                        size += 2;
                        break;
                }
            }
            return size;
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
            return fieldDefaultValues.FirstOrDefault(x => x.fieldIndex == index);
        }

        public Il2CppParameterDefaultValue GetParameterDefaultValueFromIndex(int index)
        {
            return parameterDefaultValues.FirstOrDefault(x => x.parameterIndex == index);
        }

        public int GetDefaultValueFromIndex(int index)
        {
            return metadataHeader.fieldAndParameterDefaultValueDataOffset + index;
        }

        public string GetStringFromIndex(int index)
        {
            return ReadStringToNull(metadataHeader.stringOffset + index);
        }

        public int GetCustomAttributeIndex(Il2CppAssemblyDefinition assemblyDef, int customAttributeIndex, uint token)
        {
            if (Program.MetadataVersion > 24)
            {
                var end = assemblyDef.customAttributeStart + assemblyDef.customAttributeCount;
                for (int i = assemblyDef.customAttributeStart; i < end; i++)
                {
                    if (attributeTypeRanges[i].token == token)
                    {
                        return i;
                    }
                }
                return -1;
            }
            else
            {
                return customAttributeIndex;
            }
        }

        public string GetStringLiteralFromIndex(uint index)
        {
            var stringLiteral = stringLiterals[index];
            Position = metadataHeader.stringLiteralDataOffset + stringLiteral.dataIndex;
            return Encoding.UTF8.GetString(ReadBytes((int)stringLiteral.length));
        }
    }
}