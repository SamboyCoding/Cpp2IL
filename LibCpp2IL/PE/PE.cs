using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Iced.Intel;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.PE
{
    public sealed class PE : ClassReadingBinaryReader
    {
        //Initialized in constructor
        internal readonly byte[] raw; //Internal for PlusSearch
        private readonly long maxMetadataUsages;
        
        //PE-Specific Stuff
        internal readonly SectionHeader[] peSectionHeaders; //Internal for the one use in PlusSearch
        internal readonly ulong peImageBase; //Internal for the one use in PlusSearch
        private readonly OptionalHeader64? peOptionalHeader64;
        private readonly OptionalHeader? peOptionalHeader32;
        
        //Disable null check because this stuff is initialized post-constructor
#pragma warning disable 8618
        private uint[]? peExportedFunctionPointers;
        private uint[] peExportedFunctionNamePtrs;
        private ushort[] peExportedFunctionOrdinals;
        
        //Il2cpp binary fields:
        
        //Top-level structs
        private Il2CppMetadataRegistration metadataRegistration;
        private Il2CppCodeRegistration codeRegistration;

        //Pointers
        private ulong[] methodPointers;
        private ulong[] genericMethodPointers;
        private ulong[] invokerPointers;
        private ulong[] customAttributeGenerators; //Pre-27 only
        private long[] fieldOffsets;
        private ulong[] metadataUsages; //Pre-27 only
        private ulong[][] codeGenModuleMethodPointers; //24.2+

        private Il2CppType[] types;
        private Il2CppGenericMethodFunctionsDefinitions[] genericMethodTables;
        private Il2CppGenericInst[] genericInsts;
        private Il2CppMethodSpec[] methodSpecs;
        private Il2CppCodeGenModule[] codeGenModules;
        private Il2CppTokenRangePair[][] codegenModuleRgctxRanges;
        private Il2CppRGCTXDefinition[][] codegenModuleRgctxs;

        private Dictionary<int, ulong> genericMethodDictionary;
        private readonly ConcurrentDictionary<ulong, Il2CppType> typesDict = new();
        public readonly Dictionary<Il2CppMethodDefinition, List<Il2CppConcreteGenericMethod>> ConcreteGenericMethods = new();
        public readonly Dictionary<ulong, List<Il2CppConcreteGenericMethod>> ConcreteGenericImplementationsByAddress = new();

        public PE(MemoryStream input, long maxMetadataUsages) : base(input)
        {
            raw = input.GetBuffer();
            Console.Write("Reading PE File Header...");
            var start = DateTime.Now;

            this.maxMetadataUsages = maxMetadataUsages;
            if (ReadUInt16() != 0x5A4D) //Magic number
                throw new FormatException("ERROR: Magic number mismatch.");
            Position = 0x3C; //Signature position position (lol)
            Position = ReadUInt32(); //Signature position
            if (ReadUInt32() != 0x00004550) //Signature
                throw new FormatException("ERROR: Invalid PE file signature");

            var fileHeader = ReadClass<FileHeader>(-1);
            if (fileHeader.Machine == 0x014c) //Intel 386
            {
                is32Bit = true;
                peOptionalHeader32 = ReadClass<OptionalHeader>(-1);
                peOptionalHeader32.DataDirectory = ReadClassArray<DataDirectory>(-1, peOptionalHeader32.NumberOfRvaAndSizes);
                peImageBase = peOptionalHeader32.ImageBase;
            }
            else if (fileHeader.Machine == 0x8664) //AMD64
            {
                peOptionalHeader64 = ReadClass<OptionalHeader64>(-1);
                peOptionalHeader64.DataDirectory = ReadClassArray<DataDirectory>(-1, peOptionalHeader64.NumberOfRvaAndSizes);
                peImageBase = peOptionalHeader64.ImageBase;
            }
            else
            {
                throw new NotSupportedException("ERROR: Unsupported machine.");
            }

            peSectionHeaders = new SectionHeader[fileHeader.NumberOfSections];
            for (var i = 0; i < fileHeader.NumberOfSections; i++)
            {
                peSectionHeaders[i] = new SectionHeader
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
            Console.WriteLine($"\tImage Base at 0x{peImageBase:X}");
            Console.WriteLine($"\tDLL is {(is32Bit ? "32" : "64")}-bit");
        }
#pragma warning restore 8618

        private bool AutoInit(ulong pCodeRegistration, ulong pMetadataRegistration)
        {
            Console.WriteLine($"\tCodeRegistration : 0x{pCodeRegistration:x}");
            Console.WriteLine($"\tMetadataRegistration : 0x{pMetadataRegistration:x}");
            if (pCodeRegistration == 0 || pMetadataRegistration == 0) return false;

            Init(pCodeRegistration, pMetadataRegistration);
            return true;
        }

        public long MapVirtualAddressToRaw(ulong uiAddr)
        {
            var addr = (uint) (uiAddr - peImageBase);

            if (addr == (uint) int.MaxValue + 1)
                throw new OverflowException($"Provided address, 0x{uiAddr:X}, was less than image base, 0x{peImageBase:X}");

            var last = peSectionHeaders[peSectionHeaders.Length - 1];
            if (addr > last.VirtualAddress + last.VirtualSize)
                // throw new ArgumentOutOfRangeException($"Provided address maps to image offset {addr} which is outside the range of the file (last section ends at {last.VirtualAddress + last.VirtualSize})");
                return 0L;

            var section = peSectionHeaders.FirstOrDefault(x => addr >= x.VirtualAddress && addr <= x.VirtualAddress + x.VirtualSize);

            if (section == null) return 0L;

            return addr - (section.VirtualAddress - section.PointerToRawData);
        }

        public ulong MapRawAddressToVirtual(uint offset)
        {
            var section = peSectionHeaders.First(x => offset >= x.PointerToRawData && offset < x.PointerToRawData + x.SizeOfRawData);

            return peImageBase + section.VirtualAddress + offset - section.PointerToRawData;
        }


        public bool TryMapRawAddressToVirtual(in uint offset, out ulong va)
        {
            try
            {
                va = MapRawAddressToVirtual(offset);
                return true;
            }
            catch (Exception)
            {
                va = 0;
                return false;
            }
        }

        public T[] ReadClassArrayAtVirtualAddress<T>(ulong addr, long count) where T : new()
        {
            return ReadClassArray<T>(MapVirtualAddressToRaw(addr), count);
        }

        public T ReadClassAtVirtualAddress<T>(ulong addr) where T : new()
        {
            return ReadClass<T>(MapVirtualAddressToRaw(addr));
        }

        public void Init(ulong pCodeRegistration, ulong pMetadataRegistration)
        {
            Console.WriteLine("Initializing PE data...");
            codeRegistration = ReadClassAtVirtualAddress<Il2CppCodeRegistration>(pCodeRegistration);
            metadataRegistration = ReadClassAtVirtualAddress<Il2CppMetadataRegistration>(pMetadataRegistration);

            Console.Write("\tReading generic instances...");
            var start = DateTime.Now;
            genericInsts = Array.ConvertAll(ReadClassArrayAtVirtualAddress<ulong>(metadataRegistration.genericInsts, metadataRegistration.genericInstsCount), x => ReadClassAtVirtualAddress<Il2CppGenericInst>(x));
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading generic method pointers...");
            start = DateTime.Now;
            genericMethodPointers = ReadClassArrayAtVirtualAddress<ulong>(codeRegistration.genericMethodPointers, (long) codeRegistration.genericMethodPointersCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading invoker pointers...");
            start = DateTime.Now;
            invokerPointers = ReadClassArrayAtVirtualAddress<ulong>(codeRegistration.invokerPointers, (long) codeRegistration.invokerPointersCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            if (LibCpp2IlMain.MetadataVersion < 27)
            {
                Console.Write("\tReading custom attribute generators...");
                start = DateTime.Now;
                customAttributeGenerators = ReadClassArrayAtVirtualAddress<ulong>(codeRegistration.customAttributeGeneratorListAddress, codeRegistration.customAttributeCount);
                Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            Console.Write("\tReading field offsets...");
            start = DateTime.Now;
            fieldOffsets = ReadClassArrayAtVirtualAddress<long>(metadataRegistration.fieldOffsetListAddress, metadataRegistration.fieldOffsetsCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading types...");
            start = DateTime.Now;
            var typesAddress = ReadClassArrayAtVirtualAddress<ulong>(metadataRegistration.typeAddressListAddress, metadataRegistration.numTypes);
            types = new Il2CppType[metadataRegistration.numTypes];
            for (var i = 0; i < metadataRegistration.numTypes; ++i)
            {
                types[i] = ReadClassAtVirtualAddress<Il2CppType>(typesAddress[i]);
                types[i].Init();
                typesDict[typesAddress[i]] = types[i];
            }

            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.WriteLine($"\tLast type starts at virt address 0x{typesAddress.Max():X}");

            if (metadataRegistration.metadataUsages != 0)
            {
                //Empty in v27
                Console.Write("\tReading metadata usages...");
                start = DateTime.Now;
                metadataUsages = ReadClassArrayAtVirtualAddress<ulong>(metadataRegistration.metadataUsages, maxMetadataUsages);
                Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            if (LibCpp2IlMain.MetadataVersion >= 24.2f)
            {
                Console.WriteLine("\tReading code gen modules...");
                start = DateTime.Now;

                var codeGenModulePtrs = ReadClassArrayAtVirtualAddress<ulong>(codeRegistration.addrCodeGenModulePtrs, (long) codeRegistration.codeGenModulesCount);
                codeGenModules = new Il2CppCodeGenModule[codeGenModulePtrs.Length];
                codeGenModuleMethodPointers = new ulong[codeGenModulePtrs.Length][];
                codegenModuleRgctxRanges = new Il2CppTokenRangePair[codeGenModulePtrs.Length][];
                codegenModuleRgctxs = new Il2CppRGCTXDefinition[codeGenModulePtrs.Length][];
                for (var i = 0; i < codeGenModulePtrs.Length; i++)
                {
                    var codeGenModule = ReadClassAtVirtualAddress<Il2CppCodeGenModule>(codeGenModulePtrs[i]);
                    codeGenModules[i] = codeGenModule;
                    string name = ReadStringToNull(MapVirtualAddressToRaw(codeGenModule.moduleName));
                    Console.WriteLine($"\t\t-Read module data for {name}, contains {codeGenModule.methodPointerCount} method pointers starting at 0x{codeGenModule.methodPointers:X}");
                    if (codeGenModule.methodPointerCount > 0)
                    {
                        try
                        {
                            var ptrs = ReadClassArrayAtVirtualAddress<ulong>(codeGenModule.methodPointers, codeGenModule.methodPointerCount);
                            codeGenModuleMethodPointers[i] = ptrs;
                            Console.WriteLine($"\t\t\t-Read {codeGenModule.methodPointerCount} method pointers.");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"\t\t\tWARNING: Unable to get function pointers for {name}: {e.Message}");
                            codeGenModuleMethodPointers[i] = new ulong[codeGenModule.methodPointerCount];
                        }
                    }

                    if (codeGenModule.rgctxRangesCount > 0)
                    {
                        try
                        {
                            var ranges = ReadClassArrayAtVirtualAddress<Il2CppTokenRangePair>(codeGenModule.pRgctxRanges, codeGenModule.rgctxRangesCount);
                            codegenModuleRgctxRanges[i] = ranges;
                            Console.WriteLine($"\t\t\t-Read {codeGenModule.rgctxRangesCount} RGCTX ranges.");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"\t\t\tWARNING: Unable to get RGCTX ranges for {name}: {e.Message}");
                            codegenModuleRgctxRanges[i] = new Il2CppTokenRangePair[codeGenModule.rgctxRangesCount];
                        }
                    }

                    if (codeGenModule.rgctxsCount > 0)
                    {
                        try
                        {
                            var rgctxs = ReadClassArrayAtVirtualAddress<Il2CppRGCTXDefinition>(codeGenModule.rgctxs, codeGenModule.rgctxsCount);
                            codegenModuleRgctxs[i] = rgctxs;
                            Console.WriteLine($"\t\t\t-Read {codeGenModule.rgctxsCount} RGCTXs.");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"\t\t\tWARNING: Unable to get RGCTXs for {name}: {e.Message}");
                            codegenModuleRgctxs[i] = new Il2CppRGCTXDefinition[codeGenModule.rgctxsCount];
                        }
                    }
                }

                Console.WriteLine($"\tOK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }
            else
            {
                Console.Write("\tReading method pointers...");
                start = DateTime.Now;
                methodPointers = ReadClassArrayAtVirtualAddress<ulong>(codeRegistration.methodPointers, (long) codeRegistration.methodPointersCount);
                Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }


            Console.Write("\tReading generic method tables...");
            start = DateTime.Now;
            genericMethodTables = ReadClassArrayAtVirtualAddress<Il2CppGenericMethodFunctionsDefinitions>(metadataRegistration.genericMethodTable, metadataRegistration.genericMethodTableCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading method specifications...");
            start = DateTime.Now;
            methodSpecs = ReadClassArrayAtVirtualAddress<Il2CppMethodSpec>(metadataRegistration.methodSpecs, metadataRegistration.methodSpecsCount);
            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            Console.Write("\tReading generic methods...");
            start = DateTime.Now;
            genericMethodDictionary = new Dictionary<int, ulong>(genericMethodTables.Length);
            foreach (var table in genericMethodTables)
            {
                var genericMethodIndex = table.genericMethodIndex;
                var genericMethodPointerIndex = table.indices.methodIndex;

                var methodDefIndex = GetGenericMethodFromIndex(genericMethodIndex, genericMethodPointerIndex, out _);

                if (!genericMethodDictionary.ContainsKey(methodDefIndex))
                {
                    genericMethodDictionary.Add(methodDefIndex, genericMethodPointers[genericMethodPointerIndex]);
                }
            }

            Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        private int GetGenericMethodFromIndex(int genericMethodIndex, int genericMethodPointerIndex, out Il2CppConcreteGenericMethod? concreteMethod)
        {
            var methodSpec = GetMethodSpec(genericMethodIndex);
            var methodDefIndex = methodSpec.methodDefinitionIndex;
            concreteMethod = null;
            if (methodSpec.methodIndexIndex >= 0)
            {
                var genericInst = GetGenericInst(methodSpec.methodIndexIndex);
                var ptrs = ReadClassArrayAtVirtualAddress<ulong>(genericInst.pointerStart, (long) genericInst.pointerCount);
                var genericTypes = ptrs.Select(GetIl2CppTypeFromPointer).ToArray();

                var genericParamData = genericTypes.Select(type => LibCpp2ILUtils.GetTypeReflectionData(type)!).ToArray();

                ulong concreteMethodPtr = 0;
                if (genericMethodPointerIndex >= 0)
                    concreteMethodPtr = genericMethodPointers[genericMethodPointerIndex];

                var baseMethod = LibCpp2IlMain.TheMetadata!.methodDefs[methodDefIndex];

                if (!ConcreteGenericMethods.ContainsKey(baseMethod))
                    ConcreteGenericMethods[baseMethod] = new List<Il2CppConcreteGenericMethod>();

                concreteMethod = new Il2CppConcreteGenericMethod
                {
                    BaseMethod = baseMethod,
                    GenericParams = genericParamData,
                    GenericVariantPtr = concreteMethodPtr
                };

                ConcreteGenericMethods[baseMethod].Add(concreteMethod);

                if (concreteMethodPtr > 0)
                {
                    if (!ConcreteGenericImplementationsByAddress.ContainsKey(concreteMethodPtr))
                        ConcreteGenericImplementationsByAddress[concreteMethodPtr] = new List<Il2CppConcreteGenericMethod>();
                    ConcreteGenericImplementationsByAddress[concreteMethodPtr].Add(concreteMethod);
                }
            }

            return methodDefIndex;
        }

        public bool PlusSearch(int methodCount, int typeDefinitionsCount)
        {
            Console.WriteLine("Looking for registration functions...");

            var execList = new List<SectionHeader>();
            var dataList = new List<SectionHeader>();
            foreach (var section in peSectionHeaders)
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

            ulong pCodeRegistration = 0;
            ulong pMetadataRegistration;

            Console.WriteLine("Attempting to locate code and metadata registration functions...");

            var plusSearch = new PlusSearch(this, methodCount, typeDefinitionsCount, maxMetadataUsages);
            var dataSections = dataList.ToArray();
            var execSections = execList.ToArray();
            plusSearch.SetSearch(peImageBase, dataSections);
            plusSearch.SetDataSections(peImageBase, dataSections);
            plusSearch.SetExecSections(peImageBase, execSections);

            if (LibCpp2IlMain.MetadataVersion < 27f)
            {
                if (is32Bit)
                {
                    Console.WriteLine("\t(32-bit PE)");
                    plusSearch.SetExecSections(peImageBase, dataSections);
                    pMetadataRegistration = plusSearch.FindMetadataRegistration();
                }
                else
                {
                    Console.WriteLine("\t(64-bit PE)");
                    plusSearch.SetExecSections(peImageBase, dataSections);
                    pMetadataRegistration = plusSearch.FindMetadataRegistration64Bit();
                }
            }
            else
            {
                //v27+ metadata location
                pMetadataRegistration = plusSearch.FindMetadataRegistrationV27();
            }

            if (is32Bit && pMetadataRegistration != 0)
            {
                pCodeRegistration = plusSearch.TryFindCodeRegUsingMetaReg(pMetadataRegistration);
            }

            if (pCodeRegistration == 0)
            {
                if (LibCpp2IlMain.MetadataVersion >= 24.2f)
                {
                    Console.WriteLine("\tUsing mscorlib full-disassembly approach to get codereg, this may take a while...");
                    pCodeRegistration = plusSearch.FindCodeRegistrationUsingMscorlib();
                }
                else
                    pCodeRegistration = is32Bit ? plusSearch.FindCodeRegistration() : plusSearch.FindCodeRegistration64Bit();
            }


#if ALLOW_CODEREG_FALLBACK
            if (codeRegistration == 0 || metadataRegistration == 0)
                (codeRegistration, metadataRegistration) = UseDecompilationBasedFallback();
#endif

            if (pCodeRegistration == 0 && LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput)
            {
                Console.Write("Couldn't identify a CodeRegistration address. If you know it, enter it now, otherwise enter nothing or zero to fail: ");
                var crInput = Console.ReadLine();
                ulong.TryParse(crInput, NumberStyles.HexNumber, null, out pCodeRegistration);
            }

            if (pMetadataRegistration == 0 && LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput)
            {
                Console.Write("Couldn't identify a MetadataRegistration address. If you know it, enter it now, otherwise enter nothing or zero to fail: ");
                var mrInput = Console.ReadLine();
                ulong.TryParse(mrInput, NumberStyles.HexNumber, null, out pMetadataRegistration);
            }

            Console.WriteLine("Initializing with located addresses:");
            return AutoInit(pCodeRegistration, pMetadataRegistration);
        }

        public Il2CppType GetIl2CppTypeFromPointer(ulong pointer)
        {
            return typesDict[pointer];
        }

        public ulong[] GetPointers(ulong pointer, long count)
        {
            if (is32Bit)
                return Array.ConvertAll(ReadClassArrayAtVirtualAddress<uint>(pointer, count), x => (ulong) x);
            return ReadClassArrayAtVirtualAddress<ulong>(pointer, count);
        }

        public int GetFieldOffsetFromIndex(int typeIndex, int fieldIndexInType, int fieldIndex, bool isValueType, bool isStatic)
        {
            try
            {
                var offset = -1;
                if (LibCpp2IlMain.MetadataVersion > 21)
                {
                    var ptr = (ulong) fieldOffsets[typeIndex];
                    if (ptr > 0)
                    {
                        Position = (long) ((ulong) MapVirtualAddressToRaw(ptr) + 4ul * (ulong) fieldIndexInType);
                        offset = ReadInt32();
                    }
                }
                else
                {
                    offset = (int) fieldOffsets[fieldIndex];
                }

                if (offset > 0)
                {
                    if (isValueType && !isStatic)
                    {
                        if (is32Bit)
                        {
                            offset -= 8;
                        }
                        else
                        {
                            offset -= 16;
                        }
                    }
                }

                return offset;
            }
            catch
            {
                return -1;
            }
        }

        public ulong GetMethodPointer(int methodIndex, int methodDefinitionIndex, int imageIndex, uint methodToken)
        {
            if (LibCpp2IlMain.MetadataVersion >= 24.2f)
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

        private void LoadPeExportTable()
        {
            uint addrExportTable;
            if (is32Bit)
            {
                if (peOptionalHeader32?.DataDirectory == null || peOptionalHeader32.DataDirectory.Length == 0)
                    throw new InvalidDataException("Could not load 32-bit optional header or data directory, or data directory was empty!");

                //We assume, per microsoft guidelines, that the first datadirectory is the export table.
                addrExportTable = peOptionalHeader32.DataDirectory.First().VirtualAddress;
            }
            else
            {
                if (peOptionalHeader64?.DataDirectory == null || peOptionalHeader64.DataDirectory.Length == 0)
                    throw new InvalidDataException("Could not load 64-bit optional header or data directory, or data directory was empty!");

                //We assume, per microsoft guidelines, that the first datadirectory is the export table.
                addrExportTable = peOptionalHeader64.DataDirectory.First().VirtualAddress;
            }

            //Non-virtual addresses for these
            var directoryEntryExports = ReadClassAtVirtualAddress<PeDirectoryEntryExport>(addrExportTable + peImageBase);

            peExportedFunctionPointers = ReadClassArrayAtVirtualAddress<uint>(directoryEntryExports.RawAddressOfExportTable + peImageBase, directoryEntryExports.NumberOfExports);
            peExportedFunctionNamePtrs = ReadClassArrayAtVirtualAddress<uint>(directoryEntryExports.RawAddressOfExportNameTable + peImageBase, directoryEntryExports.NumberOfExportNames);
            peExportedFunctionOrdinals = ReadClassArrayAtVirtualAddress<ushort>(directoryEntryExports.RawAddressOfExportOrdinalTable + peImageBase, directoryEntryExports.NumberOfExportNames); //This uses the name count per MSoft spec
        }

        public ulong GetVirtualAddressOfPeExportByName(string toFind)
        {
            if (peExportedFunctionPointers == null)
                LoadPeExportTable();

            var index = Array.FindIndex(peExportedFunctionNamePtrs, stringAddress =>
            {
                var rawStringAddress = MapVirtualAddressToRaw(stringAddress + peImageBase);
                string exportName = ReadStringToNull(rawStringAddress);
                return exportName == toFind;
            });

            if (index < 0)
                return 0;

            var ordinal = peExportedFunctionOrdinals[index];
            var functionPointer = peExportedFunctionPointers![ordinal];

            return functionPointer + peImageBase;
        }

        public ulong GetRVA(ulong pointer)
        {
            return pointer - peImageBase;
        }

        public bool TryMapVirtualAddressToRaw(ulong virtAddr, out long result)
        {
            try
            {
                result = MapVirtualAddressToRaw(virtAddr);
                return true;
            }
            catch (Exception)
            {
                result = 0;
                return false;
            }
        }

        public InstructionList DisassembleTextSection()
        {
            var textSection = peSectionHeaders.First(s => s.Name == ".text");
            var toDisasm = raw.SubArray((int) textSection.PointerToRawData, (int) textSection.SizeOfRawData);
            return LibCpp2ILUtils.DisassembleBytesNew(is32Bit, toDisasm, textSection.VirtualAddress + peImageBase);
        }

        public Il2CppGenericInst GetGenericInst(int index) => genericInsts[index];

        public Il2CppMethodSpec GetMethodSpec(int index) => methodSpecs[index];

        public Il2CppType GetType(int index) => types[index];

        public int NumTypes => types.Length;

        public ulong GetRawMetadataUsage(uint index) => metadataUsages[index];

        public ulong[] GetCodegenModuleMethodPointers(int codegenModuleIndex) => codeGenModuleMethodPointers[codegenModuleIndex];
        
        public Il2CppCodeGenModule? GetCodegenModuleByName(string name) => codeGenModules.FirstOrDefault(m => m.Name == name);

        public int GetCodegenModuleIndex(Il2CppCodeGenModule module) => Array.IndexOf(codeGenModules, module);

        public int GetCodegenModuleIndexByName(string name) => GetCodegenModuleByName(name) is { } module ? GetCodegenModuleIndex(module) : -1;

        public Il2CppTokenRangePair[] GetRGCTXRangePairsForModule(Il2CppCodeGenModule module) => codegenModuleRgctxRanges[GetCodegenModuleIndex(module)];

        public Il2CppRGCTXDefinition[] GetRGCTXDataForPair(Il2CppCodeGenModule module, Il2CppTokenRangePair rangePair) => codegenModuleRgctxs[GetCodegenModuleIndex(module)].Skip(rangePair.start).Take(rangePair.length).ToArray();

        public byte GetByteAtRawAddress(ulong addr) => raw[addr];

        public long RawLength => raw.Length;
    }
}