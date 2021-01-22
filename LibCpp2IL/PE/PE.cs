using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.PE
{
    public sealed class PE : ClassReadingBinaryReader
    {
#pragma warning disable 8618
//Disable null check because this stuff is initialized by reflection
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
        public Dictionary<Il2CppMethodDefinition, List<Il2CppConcreteGenericMethod>> ConcreteGenericMethods = new Dictionary<Il2CppMethodDefinition, List<Il2CppConcreteGenericMethod>>();
        public Dictionary<ulong, List<Il2CppConcreteGenericMethod>> ConcreteGenericImplementationsByAddress = new Dictionary<ulong, List<Il2CppConcreteGenericMethod>>();

        internal SectionHeader[] sections;
        internal ulong imageBase;

        public byte[] raw;

        //One of these will be present.
        private OptionalHeader64 optionalHeader64;
        private OptionalHeader optionalHeader;

        private uint[]? exportFunctionPointers;
        private uint[] exportFunctionNamePtrs;
        private ushort[] exportFunctionOrdinals;

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
                optionalHeader = ReadClass<OptionalHeader>(-1);
                optionalHeader.DataDirectory = ReadClassArray<DataDirectory>(-1, optionalHeader.NumberOfRvaAndSizes);
                imageBase = optionalHeader.ImageBase;
            }
            else if (fileHeader.Machine == 0x8664) //AMD64
            {
                optionalHeader64 = ReadClass<OptionalHeader64>(-1);
                optionalHeader64.DataDirectory = ReadClassArray<DataDirectory>(-1, optionalHeader64.NumberOfRvaAndSizes);
                imageBase = optionalHeader64.ImageBase;
            }
            else
            {
                throw new NotSupportedException("ERROR: Unsupported machine.");
            }

            sections = new SectionHeader[fileHeader.NumberOfSections];
            for (var i = 0; i < fileHeader.NumberOfSections; i++)
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
            Console.WriteLine($"\tImage Base at 0x{imageBase:X}");
            Console.WriteLine($"\tDLL is {(is32Bit ? "32" : "64")}-bit");
        }
#pragma warning restore 8618

        private bool AutoInit(ulong codeRegistration, ulong metadataRegistration)
        {
            Console.WriteLine($"\tCodeRegistration : 0x{codeRegistration:x}");
            Console.WriteLine($"\tMetadataRegistration : 0x{metadataRegistration:x}");
            if (codeRegistration == 0 || metadataRegistration == 0) return false;

            Init(codeRegistration, metadataRegistration);
            return true;
        }

        public long MapVirtualAddressToRaw(ulong uiAddr)
        {
            var addr = (uint) (uiAddr - imageBase);

            if (addr == (uint) int.MaxValue + 1)
                throw new OverflowException($"Provided address, {uiAddr}, was less than image base, {imageBase}");

            var last = sections[sections.Length - 1];
            if (addr > last.VirtualAddress + last.VirtualSize)
                // throw new ArgumentOutOfRangeException($"Provided address maps to image offset {addr} which is outside the range of the file (last section ends at {last.VirtualAddress + last.VirtualSize})");
                return 0L;

            var section = sections.FirstOrDefault(x => addr >= x.VirtualAddress && addr <= x.VirtualAddress + x.VirtualSize);

            if (section == null) return 0L;

            return addr - (section.VirtualAddress - section.PointerToRawData);
        }

        public ulong MapRawAddressToVirtual(uint offset)
        {
            var section = sections.First(x => offset >= x.PointerToRawData && offset < x.PointerToRawData + x.SizeOfRawData);

            return imageBase + section.VirtualAddress + offset - section.PointerToRawData;
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

            if (LibCpp2IlMain.MetadataVersion >= 24.2f)
            {
                Console.Write("\tReading code gen modules...");
                start = DateTime.Now;

                var codeGenModulePtrs = ReadClassArrayAtVirtualAddress<ulong>(this.codeRegistration.addrCodeGenModulePtrs, (long) this.codeRegistration.codeGenModulesCount);
                codeGenModules = new Il2CppCodeGenModule[codeGenModulePtrs.Length];
                codeGenModuleMethodPointers = new ulong[codeGenModulePtrs.Length][];
                for (int i = 0; i < codeGenModulePtrs.Length; i++)
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
                }

                Console.WriteLine($"\tOK ({(DateTime.Now - start).TotalMilliseconds} ms)");
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
                var methodSpec = methodSpecs[table.genericMethodIndex];
                var methodDefIndex = methodSpec.methodDefinitionIndex;
                if (methodSpec.methodIndexIndex >= 0)
                {
                    var genericInst = genericInsts[methodSpec.methodIndexIndex];
                    var ptrs = ReadClassArrayAtVirtualAddress<ulong>(genericInst.pointerStart, (long) genericInst.pointerCount);
                    var genericTypes = ptrs.Select(GetIl2CppTypeFromPointer).ToArray();

                    var genericParamData = genericTypes.Select(type => LibCpp2ILUtils.GetTypeReflectionData(type)!).ToArray();
                    var concreteMethodPtr = genericMethodPointers[table.indices.methodIndex];
                    var baseMethod = LibCpp2IlMain.TheMetadata!.methodDefs[methodDefIndex];

                    if (!ConcreteGenericMethods.ContainsKey(baseMethod))
                        ConcreteGenericMethods[baseMethod] = new List<Il2CppConcreteGenericMethod>();

                    if (!ConcreteGenericImplementationsByAddress.ContainsKey(concreteMethodPtr))
                        ConcreteGenericImplementationsByAddress[concreteMethodPtr] = new List<Il2CppConcreteGenericMethod>();

                    var concreteMethod = new Il2CppConcreteGenericMethod
                    {
                        BaseMethod = baseMethod,
                        GenericParams = genericParamData,
                        GenericVariantPtr = concreteMethodPtr
                    };

                    ConcreteGenericMethods[baseMethod].Add(concreteMethod);
                    ConcreteGenericImplementationsByAddress[concreteMethodPtr].Add(concreteMethod);
                }

                if (!genericMethodDictionary.ContainsKey(methodDefIndex))
                {
                    genericMethodDictionary.Add(methodDefIndex, genericMethodPointers[table.indices.methodIndex]);
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

            ulong codeRegistration = 0;
            ulong metadataRegistration;

            Console.WriteLine("Attempting to locate code and metadata registration functions...");

            var plusSearch = new PlusSearch(this, methodCount, typeDefinitionsCount, maxMetadataUsages);
            var dataSections = dataList.ToArray();
            var execSections = execList.ToArray();
            plusSearch.SetSearch(imageBase, dataSections);
            plusSearch.SetDataSections(imageBase, dataSections);
            plusSearch.SetExecSections(imageBase, execSections);
            
            if (is32Bit)
            {
                Console.WriteLine("\t(32-bit PE)");
                plusSearch.SetExecSections(imageBase, dataSections);
                metadataRegistration = plusSearch.FindMetadataRegistration();
            }
            else
            {
                Console.WriteLine("\t(64-bit PE)");
                plusSearch.SetExecSections(imageBase, dataSections);
                metadataRegistration = plusSearch.FindMetadataRegistration64Bit();
            }

            if (is32Bit && metadataRegistration != 0)
            {
                codeRegistration = plusSearch.TryFindCodeRegUsingMetaReg(metadataRegistration);
            }

            if (codeRegistration == 0)
            {
                if (LibCpp2IlMain.MetadataVersion >= 24.2f)
                {
                    Console.WriteLine("\tUsing mscorlib full-disassembly approach to get codereg, this may take a while...");
                    codeRegistration = plusSearch.FindCodeRegistrationUsingMscorlib();
                }
                else
                    codeRegistration = is32Bit ? plusSearch.FindCodeRegistration() : plusSearch.FindCodeRegistration64Bit();
            }



#if ALLOW_CODEREG_FALLBACK
            if (codeRegistration == 0 || metadataRegistration == 0)
                (codeRegistration, metadataRegistration) = UseDecompilationBasedFallback();
#endif

            if (codeRegistration == 0 && LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput)
            {
                Console.Write("Couldn't identify a CodeRegistration address. If you know it, enter it now, otherwise enter nothing or zero to fail: ");
                var crInput = Console.ReadLine();
                ulong.TryParse(crInput, NumberStyles.HexNumber, null, out codeRegistration);
            }

            if (metadataRegistration == 0 && LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput)
            {
                Console.Write("Couldn't identify a MetadataRegistration address. If you know it, enter it now, otherwise enter nothing or zero to fail: ");
                var mrInput = Console.ReadLine();
                ulong.TryParse(mrInput, NumberStyles.HexNumber, null, out metadataRegistration);
            }

            Console.WriteLine("Initializing with located addresses:");
            return AutoInit(codeRegistration, metadataRegistration);
        }

#if ALLOW_CODEREG_FALLBACK
        private (ulong codeRegistration, ulong metadataRegistration) UseDecompilationBasedFallback()
        {
            ulong codeRegistration;
            ulong metadataRegistration;
            Console.WriteLine("\tFailed to find code and metadata registration functions using primary location method (probably because we're post-2019), checking if we can use the fallback...");
            //Get export for il2cpp_init function
            var virtualAddrInit = GetVirtualAddressOfUnmanagedExportByName("il2cpp_init");
            if (virtualAddrInit <= 0)
            {
                Console.WriteLine("\tCould not find exported il2cpp_init function! Fallback method failed, execution will fail!");
                return (0, 0);
            }

            Console.WriteLine($"\tFound il2cpp_init export (resolves to virtual addr 0x{virtualAddrInit:X}), using fallback method to find Code and Metadata registration...");
            List<Instruction> initMethodBody = LibCpp2ILUtils.GetMethodBodyAtRawAddress(this, MapVirtualAddressToRaw(virtualAddrInit), false);

            //Look for a JMP for older il2cpp versions, on newer ones it appears to be a CALL
            var callToRuntimeInit = initMethodBody.Find(i => i.Mnemonic == ud_mnemonic_code.UD_Ijmp);

            if (callToRuntimeInit == null)
                callToRuntimeInit = initMethodBody.FindLast(i => i.Mnemonic == ud_mnemonic_code.UD_Icall);

            if (callToRuntimeInit == null)
            {
                Console.WriteLine("\tCould not find a call to Runtime::Init! Fallback method failed!");
                return (0, 0);
            }

            var virtualAddressRuntimeInit = LibCpp2ILUtils.GetJumpTarget(callToRuntimeInit, callToRuntimeInit.PC + virtualAddrInit);
            Console.WriteLine($"\tLocated probable Runtime::Init function at virtual addr 0x{virtualAddressRuntimeInit:X}");

            List<Instruction> methodBodyRuntimeInit = LibCpp2ILUtils.GetMethodBodyAtRawAddress(this, MapVirtualAddressToRaw(virtualAddressRuntimeInit), false);

            Disassembler.Translator.IncludeBinary = true;
            // File.WriteAllText(Path.Combine("cpp2il_out", "runtime_init_dump.txt"), string.Join('\n', methodBodyRuntimeInit.Select(i => i.ToString())));

            //This is kind of sketchy, but look for a global read (i.e an LEA where the second base is RIP), as that's the framework version read, then there's a MOV, then 4 calls, the third of which is our target.
            //So as to ensure compat with 2018, ensure we have a call before this LEA.
            var minimumIndex = methodBodyRuntimeInit.FindIndex(i => i.Mnemonic == ud_mnemonic_code.UD_Icall);

            var idx = -1;
            var indexOfFrameworkVersionLoad = methodBodyRuntimeInit.FindIndex(i => idx++ > minimumIndex && i.Mnemonic == ud_mnemonic_code.UD_Ilea && i.Operands.Last().Base == ud_type.UD_R_RIP);

            if (indexOfFrameworkVersionLoad < 0)
            {
                idx = -1;
                //Could be a mov in optimised code - mov of hard coded address to hard coded address
                indexOfFrameworkVersionLoad = methodBodyRuntimeInit.FindIndex(i => idx++ > minimumIndex && i.Mnemonic == ud_mnemonic_code.UD_Imov && i.Bytes.Length == 10 /*&& i.Operands.First().Type == ud_type.UD_OP_MEM && LibCpp2ILUtils.GetImmediateValue(i, i.Operands.Last()) != 0*/);

                if (indexOfFrameworkVersionLoad < 0)
                {
                    Console.WriteLine("\tCouldn't find framework load index, abort!");
                    return (0, 0);
                }
            }

            Console.WriteLine("\tLocated probable framework version set, instruction bytes are " + methodBodyRuntimeInit[indexOfFrameworkVersionLoad].ToString());

            var instructionsFromThatPoint = methodBodyRuntimeInit.Skip(indexOfFrameworkVersionLoad + 1).TakeWhile(i => true).ToList();
            var calls = instructionsFromThatPoint.Where(i => i.Mnemonic == ud_mnemonic_code.UD_Icall).ToList();

            if (calls.Count < 3)
            {
                Console.WriteLine("\tRuntime::Init does not call enough methods for us to locate ExecuteInitializations! Fallback failed!");
                return (0, 0);
            }

            var thirdCall = calls[2];

            //Now we have the address of the ExecuteInitializations function
            var virtAddrExecuteInit = LibCpp2ILUtils.GetJumpTarget(thirdCall, thirdCall.PC + virtualAddressRuntimeInit);
            Console.WriteLine($"\tLocated probable ExecuteInitializations function at virt addr 0x{virtAddrExecuteInit:X}");

            //Could be first or second instruction
            List<Instruction> execInitMethodBody = LibCpp2ILUtils.GetMethodBodyAtRawAddress(this, MapVirtualAddressToRaw(virtAddrExecuteInit), true);

            if (execInitMethodBody.Count < 2)
            {
                Console.WriteLine("\tToo few instructions in ExecuteInitializations! Aborting...");
                return (0, 0);
            }

            var movOfGlobalCallbackList = execInitMethodBody[0].Mnemonic == ud_mnemonic_code.UD_Imov ? execInitMethodBody[0] : execInitMethodBody[1];

            if (movOfGlobalCallbackList.Error || movOfGlobalCallbackList.Mnemonic != ud_mnemonic_code.UD_Imov)
            {
                Console.WriteLine("\tInvalid MOV instruction in ExecuteInitializations, fallback failed!");
                return (0, 0);
            }

            ulong addrGlobalCallbackList;
            if (!is32Bit)
            {
                var offset = LibCpp2ILUtils.GetOffsetFromMemoryAccess(movOfGlobalCallbackList, movOfGlobalCallbackList.Operands[1]);

                //This SHOULD be the address of the global list of callbacks il2cpp executes on boot, which should only contain one item, that being the function which invokes the code + metadata registration
                addrGlobalCallbackList = virtAddrExecuteInit + offset;
            }
            else
            {
                //32-bit, address is literal scalar.
                addrGlobalCallbackList = LibCpp2ILUtils.GetImmediateValue(movOfGlobalCallbackList, movOfGlobalCallbackList.Operands.Last());
            }

            var bytesNotToCheck = execInitMethodBody[1].Bytes;
            Console.WriteLine($"\tGot what we believe is the address of the global callback list - 0x{addrGlobalCallbackList:X}. Searching for another MOV instruction that references it within the .text segment...");

            var textSection = sections.First(s => s.Name == ".text");
            var toDisasm = raw.SubArray((int) textSection.PointerToRawData, (int) textSection.SizeOfRawData);
            var allInstructionsInTextSection = LibCpp2ILUtils.DisassembleBytes(is32Bit, toDisasm);

            Console.WriteLine($"\tDisassembled entire .text section, into {allInstructionsInTextSection.Count} instructions.");

            Instruction callbackListWrite;
            //if 32-bit, first param is a literal address (LibCpp2ILUtils.GetImmediateValue), else it's an rip offset.
            if (!is32Bit)
            {
                var allMOVs = allInstructionsInTextSection.Where(i => i.Mnemonic == ud_mnemonic_code.UD_Imov && i.Operands[0].Base == ud_type.UD_R_RIP).ToList();

                Console.WriteLine($"\t\t...of which {allMOVs.Count} are MOV instructions with a global/Rip first base");

                var references = allMOVs.AsParallel().Where(mov =>
                {
                    var rawMemoryRead = LibCpp2ILUtils.GetOffsetFromMemoryAccess(mov, mov.Operands[0]);
                    var virtMemoryRead = rawMemoryRead + textSection.VirtualAddress + imageBase;
                    return virtMemoryRead == addrGlobalCallbackList;
                }).ToList();

                Console.WriteLine($"\t\t...of which {references.Count} have a first parameter as that callback list.");

                if (references.Count != 1)
                {
                    Console.WriteLine("\tExpected only one reference, but didn't get that, fallback failed!");
                    return (0, 0);
                }

                callbackListWrite = references[0];
            }
            else
            {
                var allMOVsWhichReferenceAddress = allInstructionsInTextSection.Where(i => i.Mnemonic == ud_mnemonic_code.UD_Imov && LibCpp2ILUtils.GetImmediateValue(i, i.Operands[0]) == addrGlobalCallbackList).ToList();

                Console.WriteLine($"Found {allMOVsWhichReferenceAddress.Count} MOV instructions which reference address 0x{addrGlobalCallbackList:X} as the first operand.");

                if (allMOVsWhichReferenceAddress.Count != 1)
                {
                    Console.WriteLine("\tExpected only one reference, but didn't get that, fallback failed!");
                    return (0, 0);
                }

                callbackListWrite = allMOVsWhichReferenceAddress.First();
            }

            var virtualAddressOfInstruction = callbackListWrite.PC + imageBase + textSection.VirtualAddress - (ulong) callbackListWrite.Length;
            Console.WriteLine($"\tLocated a single write reference to callback list, therefore identified callback registration function, which must contain the instruction at virt address 0x{virtualAddressOfInstruction:X}");

            var instructionIdx = allInstructionsInTextSection.IndexOf(callbackListWrite);
            var instructionsUpToCallbackListWrite = allInstructionsInTextSection.Take(instructionIdx).ToList();
            instructionsUpToCallbackListWrite.Reverse();

            var indexOfFirstInt3 = instructionsUpToCallbackListWrite.FindIndex(i => i.Mnemonic == ud_mnemonic_code.UD_Iint3);
            var firstInstructionInRegisterCallback = instructionsUpToCallbackListWrite[indexOfFirstInt3 - 1];

            var virtAddrRegisterCallback = firstInstructionInRegisterCallback.PC + imageBase + textSection.VirtualAddress - (ulong) firstInstructionInRegisterCallback.Length;

            Console.WriteLine($"\tGot address of register callback function to be 0x{virtAddrRegisterCallback:X}");

            var callToRegisterCallback = allInstructionsInTextSection.Find(i => (i.Mnemonic == ud_mnemonic_code.UD_Icall || i.Mnemonic == ud_mnemonic_code.UD_Ijmp) && LibCpp2ILUtils.GetJumpTarget(i, imageBase + textSection.VirtualAddress + i.PC) == virtAddrRegisterCallback);

            var addrCallToRegCallback = callToRegisterCallback.PC + imageBase + textSection.VirtualAddress - (ulong) callToRegisterCallback.Length;
            Console.WriteLine($"\tFound a call to that function at 0x{addrCallToRegCallback:X}");

            var indexOfCallToRegisterCallback = allInstructionsInTextSection.IndexOf(callToRegisterCallback);
            Instruction? loadOfAddressToCodegenRegistrationFunction = null;
            for (var i = indexOfCallToRegisterCallback; i > 0; i--)
            {
                if (!is32Bit)
                {
                    if (allInstructionsInTextSection[i].Mnemonic == ud_mnemonic_code.UD_Ilea && allInstructionsInTextSection[i].Operands[0].Base == ud_type.UD_R_RDX)
                    {
                        loadOfAddressToCodegenRegistrationFunction = allInstructionsInTextSection[i];
                        break;
                    }
                }
                else
                {
                    //32-bit, look for a PUSH with a non-zero addr
                    if (allInstructionsInTextSection[i].Mnemonic == ud_mnemonic_code.UD_Ipush && LibCpp2ILUtils.GetImmediateValue(allInstructionsInTextSection[i], allInstructionsInTextSection[i].Operands.First()) != 0)
                    {
                        loadOfAddressToCodegenRegistrationFunction = allInstructionsInTextSection[i];
                        break;
                    }
                }
            }

            if (loadOfAddressToCodegenRegistrationFunction == null)
            {
                Console.WriteLine("Failed to find an instruction loading the address of the codegen reg function. Fallback failed.");
                return (0, 0);
            }

            Console.WriteLine("\tGot instruction containing the address of the codegen registration function: " + loadOfAddressToCodegenRegistrationFunction);

            ulong virtAddrS_Il2CppCodegenRegistration;
            if (!is32Bit)
                virtAddrS_Il2CppCodegenRegistration = LibCpp2ILUtils.GetOffsetFromMemoryAccess(loadOfAddressToCodegenRegistrationFunction, loadOfAddressToCodegenRegistrationFunction.Operands[1]) + imageBase + textSection.VirtualAddress;
            else
                virtAddrS_Il2CppCodegenRegistration = LibCpp2ILUtils.GetImmediateValue(loadOfAddressToCodegenRegistrationFunction, loadOfAddressToCodegenRegistrationFunction.Operands.First());

            Console.WriteLine($"\tWhich means s_Il2CppCodegenRegistration is in-binary at 0x{virtAddrS_Il2CppCodegenRegistration:X}");

            //This should consist of LEA, LEA, LEA, JMP on x64, or PUSH PUSH PUSH CALL on 32-bit
            List<Instruction> methodBodyS_Il2CppCodegenRegistration = LibCpp2ILUtils.GetMethodBodyAtRawAddress(this, MapVirtualAddressToRaw(virtAddrS_Il2CppCodegenRegistration), false);

            if (!is32Bit)
            {
                //64-bit
                var loadMetadataRegistration = methodBodyS_Il2CppCodegenRegistration.Find(i => i.Mnemonic == ud_mnemonic_code.UD_Ilea && i.Operands[0].Base == ud_type.UD_R_RDX);
                var loadCodeRegistration = methodBodyS_Il2CppCodegenRegistration.Find(i => i.Mnemonic == ud_mnemonic_code.UD_Ilea && i.Operands[0].Base == ud_type.UD_R_RCX); //This one's RCX not RDX

                metadataRegistration = LibCpp2ILUtils.GetOffsetFromMemoryAccess(loadMetadataRegistration, loadMetadataRegistration.Operands[1]) + virtAddrS_Il2CppCodegenRegistration;
                codeRegistration = LibCpp2ILUtils.GetOffsetFromMemoryAccess(loadCodeRegistration, loadCodeRegistration.Operands[1]) + virtAddrS_Il2CppCodegenRegistration;
                
                return (metadataRegistration, codeRegistration);
            }

            //Three pushes. First is the config, so is irrelevant
            //Second is metadata reg.
            //Third is code reg
            var pushes = methodBodyS_Il2CppCodegenRegistration.Where(i => i.Mnemonic == ud_mnemonic_code.UD_Ipush).ToList();

            if (pushes.Count < 3)
            {
                Console.WriteLine($"\tExpecting 3 pushes, only got {pushes.Count}, aborting!");
                return (0, 0);
            }

            metadataRegistration = LibCpp2ILUtils.GetImmediateValue(pushes[1], pushes[1].Operands.First());
            codeRegistration = LibCpp2ILUtils.GetImmediateValue(pushes[2], pushes[2].Operands.First());

            return (metadataRegistration, codeRegistration);
        }
#endif

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
                        Position = (long) ((ulong) MapVirtualAddressToRaw(ptr) + 4ul * (ulong)fieldIndexInType);
                        offset = ReadInt32();
                    }
                }
                else
                {
                    offset = (int)fieldOffsets[fieldIndex];
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

        private void LoadExportTable()
        {
            uint addrExportTable;
            if (is32Bit)
            {
                if (optionalHeader?.DataDirectory == null || optionalHeader.DataDirectory.Length == 0)
                    throw new InvalidDataException("Could not load 32-bit optional header or data directory, or data directory was empty!");

                //We assume, per microsoft guidelines, that the first datadirectory is the export table.
                addrExportTable = optionalHeader.DataDirectory.First().VirtualAddress;
            }
            else
            {
                if (optionalHeader64?.DataDirectory == null || optionalHeader64.DataDirectory.Length == 0)
                    throw new InvalidDataException("Could not load 64-bit optional header or data directory, or data directory was empty!");

                //We assume, per microsoft guidelines, that the first datadirectory is the export table.
                addrExportTable = optionalHeader64.DataDirectory.First().VirtualAddress;
            }

            //Non-virtual addresses for these
            var directoryEntryExports = ReadClassAtVirtualAddress<PeDirectoryEntryExport>(addrExportTable + imageBase);

            exportFunctionPointers = ReadClassArrayAtVirtualAddress<uint>(directoryEntryExports.RawAddressOfExportTable + imageBase, directoryEntryExports.NumberOfExports);
            exportFunctionNamePtrs = ReadClassArrayAtVirtualAddress<uint>(directoryEntryExports.RawAddressOfExportNameTable + imageBase, directoryEntryExports.NumberOfExportNames);
            exportFunctionOrdinals = ReadClassArrayAtVirtualAddress<ushort>(directoryEntryExports.RawAddressOfExportOrdinalTable + imageBase, directoryEntryExports.NumberOfExportNames); //This uses the name count per MSoft spec
        }

        public ulong GetVirtualAddressOfUnmanagedExportByName(string toFind)
        {
            if (exportFunctionPointers == null)
                LoadExportTable();

            var index = Array.FindIndex(exportFunctionNamePtrs, stringAddress =>
            {
                var rawStringAddress = MapVirtualAddressToRaw(stringAddress + imageBase);
                string exportName = ReadStringToNull(rawStringAddress);
                return exportName == toFind;
            });

            if (index < 0)
                return 0;

            var ordinal = exportFunctionOrdinals[index];
            var functionPointer = exportFunctionPointers![ordinal];

            return functionPointer + imageBase;
        }

        public ulong GetRVA(ulong pointer)
        {
            return pointer - imageBase;
        }
    }
}