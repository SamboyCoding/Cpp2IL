using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AudicaShredder.PE
{
    public sealed class PE : ClassReadingBinaryReader
    {
        private Il2CppMetadataRegistration metadataRegistration;
        private Il2CppCodeRegistration codeRegistration;
        public ulong[] methodPointers;
        public ulong[] genericMethodPointers;
        public ulong[] invokerPointers;
        public ulong[] customAttributeGenerators;
        private long[] fieldOffsets;
        public Il2CppType[] types;
        private Dictionary<ulong, Il2CppType> typesDict = new Dictionary<ulong, Il2CppType>();
        public ulong[] metadataUsages;
        private Il2CppGenericMethodFunctionsDefinitions[] genericMethodTables;
        public Il2CppGenericInst[] genericInsts;
        public Il2CppMethodSpec[] methodSpecs;
        private Dictionary<int, ulong> genericMethodDictionary;
        private long maxMetadataUsages;
        private Il2CppCodeGenModule[] codeGenModules;
        public ulong[][] codeGenModuleMethodPointers;

        private SectionHeader[] sections;
        private ulong imageBase;

        public PE(Stream input, long maxMetadataUsages) : base(input)
        {
            Console.Write("Reading PE File Header...");
            var start = DateTime.Now;

            this.maxMetadataUsages = maxMetadataUsages;
            if (ReadUInt16() != 0x5A4D) //Magic number
                throw new Exception("ERROR: Magic number mismatch.");
            Position = 0x3C; //Signature position position (lol)
            Position = ReadUInt32(); //Signature position
            if (ReadUInt32() != 0x00004550) //Signature
                throw new Exception("ERROR: Invalid PE file signature");

            var fileHeader = ReadClass<FileHeader>(-1);
            if (fileHeader.Machine == 0x014c) //Intel 386
            {
                is32Bit = true;
                var optionalHeader = ReadClass<OptionalHeader>(-1);
                optionalHeader.DataDirectory = ReadClassArray<DataDirectory>(-1, optionalHeader.NumberOfRvaAndSizes);
                imageBase = optionalHeader.ImageBase;
            }
            else if (fileHeader.Machine == 0x8664) //AMD64
            {
                var optionalHeader = ReadClass<OptionalHeader64>(-1);
                optionalHeader.DataDirectory = ReadClassArray<DataDirectory>(-1, optionalHeader.NumberOfRvaAndSizes);
                imageBase = optionalHeader.ImageBase;
            }
            else
            {
                throw new Exception("ERROR: Unsupported machine.");
            }

            sections = new SectionHeader[fileHeader.NumberOfSections];
            for (int i = 0; i < fileHeader.NumberOfSections; i++)
            {
                sections[i] = new SectionHeader
                {
                    Name = Encoding.UTF8.GetString(ReadBytes(8)).Trim('\0'),
                    VirtualSize = ReadUInt32(),
                    VirtualAddress = ReadUInt32(),
                    SizeOfRawData = ReadUInt32(),
                    PointerToRawData = ReadUInt32(),
                    PointerToRelocations = ReadUInt32(),
                    PointerToLinenumbers = ReadUInt32(),
                    NumberOfRelocations = ReadUInt16(),
                    NumberOfLinenumbers = ReadUInt16(),
                    Characteristics = ReadUInt32()
                };
            }

            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            Console.WriteLine("\tImage Base at " + imageBase);
            Console.WriteLine($"\tDLL is {(is32Bit ? "32" : "64")}-bit");
        }

        private bool AutoInit(ulong codeRegistration, ulong metadataRegistration)
        {
            Console.WriteLine("Initializing from auto-detected registration functions...");
            Console.WriteLine("\tCodeRegistration : {0:x}", codeRegistration);
            Console.WriteLine("\tMetadataRegistration : {0:x}", metadataRegistration);
            if (codeRegistration != 0 && metadataRegistration != 0)
            {
                Init(codeRegistration, metadataRegistration);
                return true;
            }

            return false;
        }

        public dynamic MapVirtualAddressToRaw(dynamic uiAddr)
        {
            var addr = (uint) (uiAddr - imageBase);
            
            if(addr == (uint) int.MaxValue + 1) 
                throw new OverflowException($"Provided address, {uiAddr}, was less than image base, {imageBase}");

            var last = sections[sections.Length - 1];
            if(addr > last.VirtualAddress + last.VirtualSize) 
                throw new ArgumentOutOfRangeException($"Provided address maps to image offset {addr} which is outside the range of the file (last section ends at {last.VirtualAddress + last.VirtualSize})");
            
            var section = sections.First(x => addr >= x.VirtualAddress && addr <= x.VirtualAddress + x.VirtualSize);
            return addr - (section.VirtualAddress - section.PointerToRawData);
        }

        public T[] ReadClassArrayAtVirtualAddress<T>(dynamic addr, long count) where T : new()
        {
            return ReadClassArray<T>(MapVirtualAddressToRaw(addr), count);
        }

        public T ReadClassAtVirtualAddress<T>(dynamic addr) where T : new()
        {
            return ReadClass<T>(MapVirtualAddressToRaw(addr));
        }

        public void Init(ulong codeRegistration, ulong metadataRegistration)
        {
            Console.WriteLine("Initializing PE data...");
            this.codeRegistration = ReadClassAtVirtualAddress<Il2CppCodeRegistration>(codeRegistration);
            this.metadataRegistration = ReadClassAtVirtualAddress<Il2CppMetadataRegistration>(metadataRegistration);

            Console.Write("\tReading generic instances...");
            var start = DateTime.Now;
            genericInsts = Array.ConvertAll(ReadClassArrayAtVirtualAddress<ulong>(this.metadataRegistration.genericInsts, this.metadataRegistration.genericInstsCount), x => ReadClassAtVirtualAddress<Il2CppGenericInst>(x));
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading generic method pointers...");
            start = DateTime.Now;
            genericMethodPointers = ReadClassArrayAtVirtualAddress<ulong>(this.codeRegistration.genericMethodPointers, (long) this.codeRegistration.genericMethodPointersCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading invoker pointers...");
            start = DateTime.Now;
            invokerPointers = ReadClassArrayAtVirtualAddress<ulong>(this.codeRegistration.invokerPointers, (long) this.codeRegistration.invokerPointersCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading custom attribute generators...");
            start = DateTime.Now;
            customAttributeGenerators = ReadClassArrayAtVirtualAddress<ulong>(this.codeRegistration.customAttributeGeneratorListAddress, this.codeRegistration.customAttributeCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading field offsets...");
            start = DateTime.Now;
            fieldOffsets = ReadClassArrayAtVirtualAddress<long>(this.metadataRegistration.fieldOffsetListAddress, this.metadataRegistration.fieldOffsetsCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading types...");
            start = DateTime.Now;
            var typesAddress = ReadClassArrayAtVirtualAddress<ulong>(this.metadataRegistration.typeAddressListAddress, this.metadataRegistration.numTypes);
            types = new Il2CppType[this.metadataRegistration.numTypes];
            for (var i = 0; i < this.metadataRegistration.numTypes; ++i)
            {
                types[i] = ReadClassAtVirtualAddress<Il2CppType>(typesAddress[i]);
                types[i].Init();
                typesDict.Add(typesAddress[i], types[i]);
            }

            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading metadata usages...");
            start = DateTime.Now;
            metadataUsages = ReadClassArrayAtVirtualAddress<ulong>(this.metadataRegistration.metadataUsages, maxMetadataUsages);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            if (Program.MetadataVersion >= 24.2f)
            {
                Console.Write("\tReading code gen modules...");
                start = DateTime.Now;

                var pCodeGenModules = ReadClassArrayAtVirtualAddress<ulong>(this.codeRegistration.codeGenModules, (long) this.codeRegistration.codeGenModulesCount);
                codeGenModules = new Il2CppCodeGenModule[pCodeGenModules.Length];
                codeGenModuleMethodPointers = new ulong[pCodeGenModules.Length][];
                for (int i = 0; i < pCodeGenModules.Length; i++)
                {
                    var codeGenModule = ReadClassAtVirtualAddress<Il2CppCodeGenModule>(pCodeGenModules[i]);
                    codeGenModules[i] = codeGenModule;
                    try
                    {
                        var ptrs = ReadClassArrayAtVirtualAddress<ulong>(codeGenModule.methodPointers, (long) codeGenModule.methodPointerCount);
                        codeGenModuleMethodPointers[i] = ptrs;
                    }
                    catch
                    {
                        Console.WriteLine($"WARNING: Unable to get function pointers for {ReadStringToNull(MapVirtualAddressToRaw(codeGenModule.moduleName))}");
                        codeGenModuleMethodPointers[i] = new ulong[codeGenModule.methodPointerCount];
                    }
                }

                Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }
            else
            {
                Console.Write("\tReading method pointers...");
                start = DateTime.Now;
                methodPointers = ReadClassArrayAtVirtualAddress<ulong>(this.codeRegistration.methodPointers, (long) this.codeRegistration.methodPointersCount);
                Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }


            Console.Write("\tReading generic method tables...");
            start = DateTime.Now;
            genericMethodTables = ReadClassArrayAtVirtualAddress<Il2CppGenericMethodFunctionsDefinitions>(this.metadataRegistration.genericMethodTable, this.metadataRegistration.genericMethodTableCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading method specifications...");
            start = DateTime.Now;
            methodSpecs = ReadClassArrayAtVirtualAddress<Il2CppMethodSpec>(this.metadataRegistration.methodSpecs, this.metadataRegistration.methodSpecsCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading generic methods...");
            start = DateTime.Now;
            genericMethodDictionary = new Dictionary<int, ulong>(genericMethodTables.Length);
            foreach (var table in genericMethodTables)
            {
                var index = methodSpecs[table.genericMethodIndex].methodDefinitionIndex;
                if (!genericMethodDictionary.ContainsKey(index))
                {
                        genericMethodDictionary.Add(index, genericMethodPointers[table.indices.methodIndex]);
                }
            }
            
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        public bool PlusSearch(int methodCount, int typeDefinitionsCount)
        {
            Console.WriteLine("Looking for registration functions...");

            var execList = new List<SectionHeader>();
            var dataList = new List<SectionHeader>();
            foreach (var section in sections)
            {
                switch (section.Characteristics)
                {
                    case 0x60000020:
                        Console.WriteLine("\tIdentified execute section " + section.Name);
                        execList.Add(section);
                        break;
                    case 0x40000040:
                    case 0xC0000040:
                        Console.WriteLine("\tIdentified data section " + section.Name);
                        dataList.Add(section);
                        break;
                }
            }

            ulong codeRegistration;
            ulong metadataRegistration;
            var plusSearch = new PlusSearch(this, methodCount, typeDefinitionsCount, maxMetadataUsages);
            var dataSections = dataList.ToArray();
            var execSections = execList.ToArray();
            plusSearch.SetSearch(imageBase, dataSections);
            plusSearch.SetDataSections(imageBase, dataSections);
            plusSearch.SetExecSections(imageBase, execSections);
            if (is32Bit)
            {
                codeRegistration = plusSearch.FindCodeRegistration();
                plusSearch.SetExecSections(imageBase, dataSections);
                metadataRegistration = plusSearch.FindMetadataRegistration();
            }
            else
            {
                codeRegistration = plusSearch.FindCodeRegistration64Bit();
                plusSearch.SetExecSections(imageBase, dataSections);
                metadataRegistration = plusSearch.FindMetadataRegistration64Bit();
            }

            return AutoInit(codeRegistration, metadataRegistration);
        }

        public Il2CppType GetIl2CppType(ulong pointer)
        {
            return typesDict[pointer];
        }

        public ulong[] GetPointers(ulong pointer, long count)
        {
            if (is32Bit)
                return Array.ConvertAll(ReadClassArrayAtVirtualAddress<uint>(pointer, count), x => (ulong) x);
            return ReadClassArrayAtVirtualAddress<ulong>(pointer, count);
        }

        public long GetFieldOffsetFromIndex(int typeIndex, int fieldIndexInType, int fieldIndex)
        {
            var ptr = fieldOffsets[typeIndex];
            if (ptr >= 0)
            {
                dynamic pos;
                if (is32Bit)
                    pos = MapVirtualAddressToRaw((uint) ptr) + 4 * fieldIndexInType;
                else
                    pos = MapVirtualAddressToRaw((ulong) ptr) + 4ul * (ulong) fieldIndexInType;
                if ((long) pos <= BaseStream.Length - 4)
                {
                    Position = pos;
                    return ReadInt32();
                }

                return -1;
            }

            return 0;
        }
        
        public ulong GetMethodPointer(int methodIndex, int methodDefinitionIndex, int imageIndex, uint methodToken)
        {
            if (Program.MetadataVersion >= 24.2f)
            {
                if (genericMethodDictionary.TryGetValue(methodDefinitionIndex, out var methodPointer))
                {
                    return methodPointer;
                }

                var ptrs = codeGenModuleMethodPointers[imageIndex];
                var methodPointerIndex = methodToken & 0x00FFFFFFu;
                return ptrs[methodPointerIndex - 1];
            }
            else
            {
                if (methodIndex >= 0)
                {
                    return methodPointers[methodIndex];
                }
                genericMethodDictionary.TryGetValue(methodDefinitionIndex, out var methodPointer);
                return methodPointer;
            }
        }
    }
}