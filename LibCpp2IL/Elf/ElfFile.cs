using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Logging;
using LibCpp2IL.PE;

namespace LibCpp2IL.Elf
{
    public sealed class ElfFile : Il2CppBinary
    {
        private byte[] _raw;
        private List<IElfProgramHeaderEntry> _elfProgramHeaderEntries;
        private readonly List<ElfSectionHeaderEntry> _elfSectionHeaderEntries;
        private ElfFileIdent? _elfFileIdent;
        private ElfFileHeader? _elfHeader;
        private readonly List<ElfDynamicEntry> _dynamicSection = new();
        private readonly List<ElfSymbolTableEntry> _symbolTable = new();
        private readonly Dictionary<string, ElfSymbolTableEntry> _exportTable = new();
        private List<long>? _initializerPointers;

        private readonly List<(ulong start, ulong end)> relocationBlocks = new(); 

        private long _globalOffset;

        public ElfFile(MemoryStream input, long maxMetadataUsages) : base(input, maxMetadataUsages)
        {
            _raw = input.GetBuffer();

            LibLogger.Verbose("\tReading Elf File Ident...");
            var start = DateTime.Now;

            ReadAndValidateIdent();

            var isBigEndian = _elfFileIdent!.Endianness == 2;

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            LibLogger.VerboseNewline($"\tBinary is {(is32Bit ? "32-bit" : "64-bit")} and {(isBigEndian ? "big-endian" : "little-endian")}.");

            if (isBigEndian)
                SetBigEndian();

            LibLogger.Verbose("\tReading and validating full ELF header...");
            start = DateTime.Now;

            ReadHeader();

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            LibLogger.VerboseNewline($"\tElf File contains instructions of type {InstructionSet}");

            LibLogger.Verbose("\tReading ELF program header table...");
            start = DateTime.Now;

            ReadProgramHeaderTable();

            LibLogger.VerboseNewline($"Read {_elfProgramHeaderEntries!.Count} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.VerboseNewline("\tReading ELF section header table and names...");
            start = DateTime.Now;

            //Non-null assertion reason: The elf header has already been checked while reading the program header.
            _elfSectionHeaderEntries = ReadClassArrayAtRawAddr<ElfSectionHeaderEntry>(_elfHeader!.pSectionHeader, _elfHeader.SectionHeaderEntryCount).ToList();

            if (_elfHeader.SectionNameSectionOffset >= 0 && _elfHeader.SectionNameSectionOffset < _elfSectionHeaderEntries.Count)
            {
                var pSectionHeaderStringTable = _elfSectionHeaderEntries[_elfHeader.SectionNameSectionOffset].RawAddress;

                foreach (var section in _elfSectionHeaderEntries)
                {
                    section.Name = ReadStringToNull(pSectionHeaderStringTable + section.NameOffset);
                    LibLogger.VerboseNewline($"\t\t-Name for section at 0x{section.RawAddress:X} is {section.Name}");
                }
            }

            LibLogger.VerboseNewline($"\tRead {_elfSectionHeaderEntries.Count} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            if (_elfSectionHeaderEntries.FirstOrDefault(s => s.Name == ".text") is { } textSection)
            {
                _globalOffset = (long) textSection.VirtualAddress - (long) textSection.RawAddress;
            }
            else
            {
                var execSegment = _elfProgramHeaderEntries!.First(p => (p.Flags & ElfProgramHeaderFlags.PF_X) != 0);
                _globalOffset = (long)execSegment.VirtualAddress - (long)execSegment.RawAddress;
            }

            //Get dynamic section.
            if (GetProgramHeaderOfType(ElfProgramEntryType.PT_DYNAMIC) is { } dynamicSegment)
                _dynamicSection = ReadClassArrayAtRawAddr<ElfDynamicEntry>(dynamicSegment.RawAddress, dynamicSegment.RawSize / (is32Bit ? 8ul : 16ul)).ToList();

            LibLogger.VerboseNewline("\tFinding Relocations...");
            start = DateTime.Now;

            ProcessRelocations();

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.VerboseNewline("\tProcessing Symbols...");
            start = DateTime.Now;

            ProcessSymbols();

            LibLogger.VerboseNewline($"\tOK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            
            LibLogger.Verbose("\tProcessing Initializers...");
            start = DateTime.Now;

            ProcessInitializers();

            LibLogger.VerboseNewline($"Got {_initializerPointers!.Count} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        private void ReadAndValidateIdent()
        {
            _elfFileIdent = ReadClassAtRawAddr<ElfFileIdent>(0);

            if (_elfFileIdent.Magic != 0x464c457f) //Magic number
                throw new FormatException("ERROR: Magic number mismatch.");

            if (_elfFileIdent.Architecture == 1)
                is32Bit = true;
            else if (_elfFileIdent.Architecture != 2)
                throw new FormatException($"Invalid arch number (expecting 1 or 2): {_elfFileIdent.Architecture}");

            if (_elfFileIdent.Version != 1)
                throw new FormatException($"ELF Version is not 1? File header has version {_elfFileIdent.Version}");
        }

        private void ReadHeader()
        {
            _elfHeader = ReadClassAtRawAddr<ElfFileHeader>(0x10);

            InstructionSet = _elfHeader.Machine switch
            {
                0x03 => InstructionSet.X86_32,
                0x3E => InstructionSet.X86_64,
                0x28 => InstructionSet.ARM32,
                0xB7 => InstructionSet.ARM64,
                _ => throw new NotImplementedException($"ELF Machine {_elfHeader.Machine} not implemented")
            };

            if (_elfHeader.Version != 1)
                throw new FormatException($"Full ELF header specifies version {_elfHeader.Version}, only supported version is 1.");
        }

        private void ReadProgramHeaderTable()
        {
            _elfProgramHeaderEntries = is32Bit
                ? ReadClassArrayAtRawAddr<ElfProgramHeaderEntry32>(_elfHeader!.pProgramHeader, _elfHeader.ProgramHeaderEntryCount).Cast<IElfProgramHeaderEntry>().ToList()
                : ReadClassArrayAtRawAddr<ElfProgramHeaderEntry64>(_elfHeader!.pProgramHeader, _elfHeader.ProgramHeaderEntryCount).Cast<IElfProgramHeaderEntry>().ToList();
        }

        private IElfProgramHeaderEntry? GetProgramHeaderOfType(ElfProgramEntryType type) => _elfProgramHeaderEntries.FirstOrDefault(p => p.Type == type);

        private IEnumerable<ElfSectionHeaderEntry> GetSections(ElfSectionEntryType type) => _elfSectionHeaderEntries.Where(s => s.Type == type);

        private ElfSectionHeaderEntry? GetSingleSection(ElfSectionEntryType type) => GetSections(type).FirstOrDefault();

        private ElfDynamicEntry? GetDynamicEntryOfType(ElfDynamicType type) => _dynamicSection.FirstOrDefault(d => d.Tag == type);

        private void ProcessRelocations()
        {
            try
            {
                var rels = new HashSet<ElfRelocation>();

                var relSectionStarts = new List<ulong>();

                //REL tables
                foreach (var section in GetSections(ElfSectionEntryType.SHT_REL))
                {
                    //Get related section pointer
                    var relatedTablePointer = _elfSectionHeaderEntries[section.LinkedSectionIndex].RawAddress;

                    //Read rel table
                    var table = ReadClassArrayAtRawAddr<ElfRelEntry>(section.RawAddress, section.Size / (ulong) section.EntrySize);

                    LibLogger.VerboseNewline($"\t\t-Got {table.Length} from REL section {section.Name}");
                    
                    relocationBlocks.Add((section.RawAddress, section.RawAddress + section.Size));
                    relSectionStarts.Add(section.RawAddress);

                    //Insert into rels list.
                    rels.UnionWith(table.Select(r => new ElfRelocation(this, r, relatedTablePointer)));
                }

                //RELA tables
                foreach (var section in GetSections(ElfSectionEntryType.SHT_RELA))
                {
                    //Get related section pointer
                    var relatedTablePointer = _elfSectionHeaderEntries[section.LinkedSectionIndex].RawAddress;

                    //Read rela table
                    var table = ReadClassArrayAtRawAddr<ElfRelaEntry>(section.RawAddress, section.Size / (ulong) section.EntrySize);

                    LibLogger.VerboseNewline($"\t\t-Got {table.Length} from RELA section {section.Name}");
                    
                    relocationBlocks.Add((section.RawAddress, section.RawAddress + section.Size));

                    //Insert into rels list.
                    rels.UnionWith(table.Select(r => new ElfRelocation(this, r, relatedTablePointer)));
                }

                //Dynamic Rel Table
                if (GetDynamicEntryOfType(ElfDynamicType.DT_REL) is { } dt_rel&& (uint) MapVirtualAddressToRaw(dt_rel.Value) is {} dtRelStartAddr && !relSectionStarts.Contains(dtRelStartAddr))
                {
                    //Null-assertion reason: We must have both a RELSZ and a RELENT or this is an error.
                    var relocationSectionSize = GetDynamicEntryOfType(ElfDynamicType.DT_RELSZ)!.Value;
                    var relCount = (int) (relocationSectionSize / GetDynamicEntryOfType(ElfDynamicType.DT_RELENT)!.Value);
                    var entries = ReadClassArrayAtRawAddr<ElfRelEntry>(dtRelStartAddr, relCount);

                    LibLogger.VerboseNewline($"\t\t-Got {entries.Length} from dynamic REL section.");

                    //Null-assertion reason: We must have a DT_SYMTAB if we have a DT_REL
                    var pSymTab = GetDynamicEntryOfType(ElfDynamicType.DT_SYMTAB)!.Value;
                    
                    relocationBlocks.Add((dtRelStartAddr, dtRelStartAddr + relocationSectionSize));

                    rels.UnionWith(entries.Select(r => new ElfRelocation(this, r, pSymTab)));
                }

                //Dynamic Rela Table
                if (GetDynamicEntryOfType(ElfDynamicType.DT_RELA) is { } dt_rela)
                {
                    //Null-assertion reason: We must have both a RELSZ and a RELENT or this is an error.
                    var relocationSectionSize = GetDynamicEntryOfType(ElfDynamicType.DT_RELASZ)!.Value;
                    var relCount = (int) (relocationSectionSize / GetDynamicEntryOfType(ElfDynamicType.DT_RELAENT)!.Value);
                    var startAddr = (uint) MapVirtualAddressToRaw(dt_rela.Value);
                    var entries = ReadClassArrayAtRawAddr<ElfRelaEntry>(startAddr, relCount);

                    LibLogger.VerboseNewline($"\t\t-Got {entries.Length} from dynamic RELA section.");

                    //Null-assertion reason: We must have a DT_SYMTAB if we have a DT_RELA
                    var pSymTab = GetDynamicEntryOfType(ElfDynamicType.DT_SYMTAB)!.Value;
                    
                    relocationBlocks.Add((startAddr, startAddr + relocationSectionSize));

                    rels.UnionWith(entries.Select(r => new ElfRelocation(this, r, pSymTab)));
                }

                var sizeOfRelocationStruct = (ulong) (is32Bit ? LibCpp2ILUtils.VersionAwareSizeOf(typeof(ElfDynamicSymbol32), true, false) : LibCpp2ILUtils.VersionAwareSizeOf(typeof(ElfDynamicSymbol64), true, false));

                LibLogger.Verbose($"\t-Now Processing {rels.Count} relocations...");

                foreach (var rel in rels)
                {
                    var pointer = rel.pRelatedSymbolTable + rel.IndexInSymbolTable * sizeOfRelocationStruct;
                    ulong symValue;
                    try
                    {
                        symValue = ((IElfDynamicSymbol) (is32Bit ? ReadClassAtRawAddr<ElfDynamicSymbol32>((long) pointer) : ReadClassAtRawAddr<ElfDynamicSymbol64>((long) pointer))).Value;
                    }
                    catch
                    {
                        LibLogger.ErrorNewline($"Exception reading dynamic symbol for rel of type {rel.Type} at pointer 0x{pointer:X} (length of file is 0x{RawLength:X}, pointer - length is 0x{pointer - (ulong) RawLength:X})");
                        throw;
                    }

                    long targetLocation;
                    try
                    {
                        targetLocation = MapVirtualAddressToRaw(rel.Offset);
                    }
                    catch (InvalidOperationException)
                    {
                        continue; //Ignore this rel.
                    }

                    //Read one word.
                    var addend = rel.Addend ?? ReadClassAtRawAddr<ulong>(targetLocation);

                    //Adapted from Il2CppInspector. Thanks to djKaty.
                    (ulong newValue, bool recognized) result = (rel.Type, InstructionSet) switch
                    {
                        (ElfRelocationType.R_ARM_ABS32, InstructionSet.ARM32) => (symValue + addend, true), // S + A
                        (ElfRelocationType.R_ARM_REL32, InstructionSet.ARM32) => (symValue + rel.Offset - addend, true), // S - P + A
                        (ElfRelocationType.R_ARM_COPY, InstructionSet.ARM32) => (symValue, true), // S

                        (ElfRelocationType.R_AARCH64_ABS64, InstructionSet.ARM64) => (symValue + addend, true), // S + A
                        (ElfRelocationType.R_AARCH64_PREL64, InstructionSet.ARM64) => (symValue + addend - rel.Offset, true), // S + A - P
                        (ElfRelocationType.R_AARCH64_GLOB_DAT, InstructionSet.ARM64) => (symValue + addend, true), // S + A
                        (ElfRelocationType.R_AARCH64_JUMP_SLOT, InstructionSet.ARM64) => (symValue + addend, true), // S + A
                        (ElfRelocationType.R_AARCH64_RELATIVE, InstructionSet.ARM64) => (symValue + addend, true), // Delta(S) + A

                        (ElfRelocationType.R_386_32, InstructionSet.X86_32) => (symValue + addend, true), // S + A
                        (ElfRelocationType.R_386_PC32, InstructionSet.X86_32) => (symValue + addend - rel.Offset, true), // S + A - P
                        (ElfRelocationType.R_386_GLOB_DAT, InstructionSet.X86_32) => (symValue, true), // S
                        (ElfRelocationType.R_386_JMP_SLOT, InstructionSet.X86_32) => (symValue, true), // S

                        (ElfRelocationType.R_AMD64_64, InstructionSet.X86_64) => (symValue + addend, true), // S + A

                        _ => (0, false)
                    };

                    if (result.recognized)
                    {
                        WriteWord((int) targetLocation, result.newValue);
                    }
                }
            }
            catch
            {
                LibLogger.Info("Exception during relocation mapping!");
                throw;
            }
        }

        private void ProcessSymbols()
        {
            var symbolTables = new List<(ulong offset, ulong count, ulong strings)>();

            //Look for .strtab
            if (GetSingleSection(ElfSectionEntryType.SHT_STRTAB) is { } strTab)
            {
                //Look for .symtab
                if (GetSingleSection(ElfSectionEntryType.SHT_SYMTAB) is { } symtab)
                {
                    LibLogger.VerboseNewline($"\t\t-Found .symtab at 0x{symtab.RawAddress:X}");
                    symbolTables.Add((symtab.RawAddress, symtab.Size / (ulong) symtab.EntrySize, strTab.RawAddress));
                }

                //Look for .dynsym
                if (GetSingleSection(ElfSectionEntryType.SHT_DYNSYM) is { } dynsym)
                {
                    LibLogger.VerboseNewline($"\t\t-Found .dynsym at 0x{dynsym.RawAddress:X}");
                    symbolTables.Add((dynsym.RawAddress, dynsym.Size / (ulong) dynsym.EntrySize, strTab.RawAddress));
                }
            }

            //Look for Dynamic String table
            if (GetDynamicEntryOfType(ElfDynamicType.DT_STRTAB) is { } dynamicStrTab)
            {
                if (GetDynamicEntryOfType(ElfDynamicType.DT_SYMTAB) is { } dynamicSymTab)
                {
                    var end = _dynamicSection.Where(x => x.Value > dynamicSymTab.Value).OrderBy(x => x.Value).First().Value;
                    var dynSymSize = (ulong) LibCpp2ILUtils.VersionAwareSizeOf(is32Bit ? typeof(ElfDynamicSymbol32) : typeof(ElfDynamicSymbol64), true, false);

                    var address = (ulong) MapVirtualAddressToRaw(dynamicSymTab.Value);

                    LibLogger.VerboseNewline($"\t\t-Found DT_SYMTAB at 0x{address:X}");

                    symbolTables.Add((
                        address,
                        (end - dynamicSymTab.Value) / dynSymSize,
                        dynamicStrTab.Value
                    ));
                }
            }

            _symbolTable.Clear();
            _exportTable.Clear();

            //Unify symbol tables
            foreach (var (offset, count, stringTable) in symbolTables)
            {
                var symbols = is32Bit
                    ? ReadClassArrayAtRawAddr<ElfDynamicSymbol32>(offset, count).Cast<IElfDynamicSymbol>().ToList()
                    : ReadClassArrayAtRawAddr<ElfDynamicSymbol64>(offset, count).Cast<IElfDynamicSymbol>().ToList();

                LibLogger.VerboseNewline($"\t\t-Found {symbols.Count} symbols in table at 0x{offset:X}");

                foreach (var symbol in symbols)
                {
                    string name;
                    try
                    {
                        name = ReadStringToNull(stringTable + symbol.NameOffset);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Stripped
                        continue;
                    }

                    var usefulType = symbol.Shndx == 0 ? ElfSymbolTableEntry.ElfSymbolEntryType.IMPORT
                        : symbol.Type == ElfDynamicSymbolType.STT_FUNC ? ElfSymbolTableEntry.ElfSymbolEntryType.FUNCTION
                        : symbol.Type == ElfDynamicSymbolType.STT_OBJECT || symbol.Type == ElfDynamicSymbolType.STT_COMMON ? ElfSymbolTableEntry.ElfSymbolEntryType.NAME
                        : ElfSymbolTableEntry.ElfSymbolEntryType.UNKNOWN;

                    var virtualAddress = symbol.Value;

                    var entry = new ElfSymbolTableEntry {Name = name, Type = usefulType, VirtualAddress = virtualAddress};
                    _symbolTable.Add(entry);

                    if (symbol.Shndx != 0)
                        _exportTable.TryAdd(name, entry);
                }
            }
        }

        private void ProcessInitializers()
        {
            if (!(GetDynamicEntryOfType(ElfDynamicType.DT_INIT_ARRAY) is { } dtInitArray) || !(GetDynamicEntryOfType(ElfDynamicType.DT_INIT_ARRAYSZ) is { } dtInitArraySz))
                return;

            var pInitArray = (ulong) MapVirtualAddressToRaw(dtInitArray.Value);
            var count = dtInitArraySz.Value / (ulong) (is32Bit ? 4 : 8);

            var initArray = ReadClassArrayAtRawAddr<ulong>(pInitArray, count);

            if (GetDynamicEntryOfType(ElfDynamicType.DT_INIT) is { } dtInit)
                initArray = initArray.Append(dtInit.Value).ToArray();

            _initializerPointers = initArray.Select(MapVirtualAddressToRaw).ToList();
        }

        public (ulong codeReg, ulong metaReg) FindCodeAndMetadataReg()
        {
            //Let's just try and be cheap here and find them in the symbol table.

            LibLogger.Verbose("\tChecking ELF Symbol Table for code and/or meta reg...");
            ulong codeReg = 0;
            ulong metadataReg = 0;
            if (_symbolTable.FirstOrDefault(s => s.Name.Contains("g_CodeRegistration")) is { } codeRegSymbol)
                codeReg = codeRegSymbol.VirtualAddress;

            if (_symbolTable.FirstOrDefault(s => s.Name.Contains("g_MetadataRegistration")) is { } metaRegSymbol)
                metadataReg = metaRegSymbol.VirtualAddress;

            if (codeReg != 0 && metadataReg != 0)
            {
                LibLogger.VerboseNewline("Found them.");
                return (codeReg, metadataReg);
            }

            LibLogger.VerboseNewline("Didn't find them, scanning binary...");
            
            //Well, that didn't work. Look for the specific initializer function which calls into Il2CppCodegenRegistration.
            return InstructionSet switch
            {
                InstructionSet.ARM32 => FindCodeAndMetadataRegArm32(),
                InstructionSet.ARM64 when LibCpp2IlMain.MetadataVersion < 24.2f => FindCodeAndMetadataRegArm64(),
                _ => FindCodeAndMetadataRegDefaultBehavior(),
            };
        }

        private (ulong codeReg, ulong metaReg) FindCodeAndMetadataRegArm32()
        {
            //This is a little complicated, so:
            //All ARM instructions are four bytes.
            //We need to check for two out of a specific 6 instructions, so 24 (0x18) bytes.
            //And we need to do this for all initializer functions.

            //Specifically, we're looking for:
            //ADD r0, pc, r0 (00 00 8f e0)
            //ADD r1, pc, r1 (01 10 8f e0)
            var addSearchBytes = new byte[] {0x00, 0x00, 0x8F, 0xE0, 0x01, 0x10, 0x8F, 0xE0};

            //Also, the third instruction should be LDR R1, #x. But we don't know what x is, but it contains the pointer to the CodegenRegistration function.
            //So search for the bytes that *don't* specify what x is. There are three.
            var ldrSearchBytes = new byte[] {0x10, 0x9F, 0xE5};

            LibLogger.VerboseNewline($"\tARM-32 MODE: Checking {_initializerPointers!.Count} initializer pointers...");
            foreach (var initializerPointer in _initializerPointers!) //Not-null asserted because it's initialized in the constructor.
            {
                //So, read 0x18 bytes.
                var instructionBytes = ReadByteArrayAtRawAddress(initializerPointer, 0x18);

                //We only want the last two instructions, so skip the first 16 bytes.
                if (!addSearchBytes.SequenceEqual(instructionBytes.Skip(0x10)))
                    continue;

                //Check last three bytes of third instruction (so skip 9 bytes, read 3) 
                if (!ldrSearchBytes.SequenceEqual(instructionBytes.Skip(9).Take(3)))
                    continue;

                //Take the 8th byte, which contains our 'x' value, which specifies where the codereg function is.
                //Add the current PC value. ARM is weird in that the PC points to two instructions *after* the currently executing one.
                //This instruction is offset 0x8, each instruction is 4 bytes, so two instructions below is 0x10 into the function.
                //So add 0x10, to the function address, to the value in byte 8.
                var pointerToPointerToCodegenRegFunction = instructionBytes[8] + initializerPointer + 0x10;

                //Now we know where the function pointer is. Or, the important part of it.
                //Read 4 bytes there.
                var pointerToCodegenRegFunction = ReadClassAtRawAddr<uint>(pointerToPointerToCodegenRegFunction);
                
                //Pointer is relative, so add on address of function + offset of pointer table (?) in function (0x1C).
                pointerToCodegenRegFunction += (uint) initializerPointer + 0x1C;
                
                //Read 7 instructions + 3 pointers which should hopefully make up Il2CppCodegenRegistration.
                //functionBody[0] through [6] are instructions, [7] through [9] are pointers.
                var functionBody = ReadClassArrayAtRawAddr<uint>(pointerToCodegenRegFunction, 10);
                
                //Check the last instruction is an unconditional branch
                if(functionBody[6].Bits(24, 8) != 0b_1110_1010)
                    continue;

                //Read the three register-value pairs for the first 3 LDRs in the function.
                var registerOffsets = new uint[3];

                var fail = false;
                for (var i = 0u; i <= 2u && !fail; i++)
                {
                    var (registerNum, immediate) = ArmUtils.GetOperandsForLiteralLdr(functionBody[i]);
                    if (registerNum > 2 || immediate == 0) //Immediate = 0 is a fail, register > 2 indicates wrong function.
                        fail = true;
                    else
                        registerOffsets[registerNum] = immediate + i * 4 + 8; //PC is +8, i*4 is 4 bytes per instruction.
                }
                
                if(fail)
                    continue;
                
                //Instructions 3-5 (4, 5, 6) load the actual data values. They can be LDR or ADD, where:
                //LDR indicates we have a pointer-to-pointer and have to read the struct pointer from elsewhere in the binary.
                //ADD indicates we have a relative pointer to the data and just resolve that to the struct pointer.

                var pointers = new uint[3];

                for (var i = 3u; i <= 5 && !fail; i++)
                {
                    var (addFirstReg, addSecondReg, addThirdReg) = ArmUtils.GetOperandsForRegisterAdd(functionBody[i]);
                    var (ldrFirstReg, ldrSecondReg, ldrThirdReg) = ArmUtils.GetOperandsForRegisterLdr(functionBody[i]);

                    if (addSecondReg == ArmUtils.PC_REG && addFirstReg == addThirdReg && addFirstReg <= 2)
                        //Valid ADD
                        pointers[addFirstReg] = pointerToCodegenRegFunction + i * 4 + functionBody[registerOffsets[addFirstReg] / 4] + 8;
                    else if (ldrSecondReg == ArmUtils.PC_REG && ldrFirstReg == ldrThirdReg && ldrFirstReg <= 2)
                    {
                        //Valid LDR.
                        var p = pointerToCodegenRegFunction + i * 4 + functionBody[registerOffsets[ldrFirstReg] / 4] + 8;
                        //VIRTUAL address
                        pointers[ldrFirstReg] = ReadClassAtVirtualAddress<uint>(p);
                    }
                    else
                        fail = true;
                }

                if (fail)
                {
                    LibLogger.VerboseNewline($"\t\tInitializer function at 0x{initializerPointer:X} is probably NOT the il2cpp initializer.");
                    continue;
                }

                LibLogger.VerboseNewline($"\t\tFound valid sequence of bytes for il2cpp initializer function at 0x{initializerPointer:X}.");

                return (pointers[0], pointers[1]);
            }

            return (0, 0);
        }

        private (ulong codeReg, ulong metaReg) FindCodeAndMetadataRegArm64()
        {
            LibLogger.VerboseNewline($"\tARM-64 MODE: Checking {_initializerPointers!.Count} initializer pointers...");
            foreach (var initializerPointer in _initializerPointers)
            {
                //In most cases we don't need more than the first 7 instructions
                var func = Arm64Utils.ReadFunctionAtRawAddress(this, (uint)initializerPointer, 7);

                //Don't accept anything longer than 7 instructions
                //I.e. if it doesn't end with a jump we don't want it 
                if (!Arm64Utils.IsB(func[^1]))
                    continue;

                var registers = Arm64Utils.GetAddressesLoadedIntoRegisters(func, (ulong) (_globalOffset + initializerPointer), this);
                
                //Did we find the initializer defined in Il2CppCodeRegistration.cpp?
                //It will have only x0 and x1 set.
                if (registers.Count == 2 && registers.ContainsKey(0) && registers.TryGetValue(1, out var x1))
                {
                    //Load the function whose address is in X1
                    var secondFunc = Arm64Utils.ReadFunctionAtRawAddress(this, (uint)MapVirtualAddressToRaw(x1), 7);

                    if (!Arm64Utils.IsB(secondFunc[^1]))
                        continue;

                    registers = Arm64Utils.GetAddressesLoadedIntoRegisters(secondFunc, x1, this);
                }
                
                //Do we have Il2CppCodegenRegistration?
                //In v21 and later - which is the only range we support - we have X0 through X2 and only those.
                //We want what's in x0 and x1. x2 is irrelevant.
                if (registers.Count == 3 && registers.TryGetValue(0, out var x0) && registers.TryGetValue(1, out x1) && registers.ContainsKey(2))
                {
                    LibLogger.VerboseNewline($"\t\tFound valid sequence of bytes for il2cpp initializer function at 0x{initializerPointer:X}.");
                    return (x0, x1);
                }

                //Fail, move on.
                
                LibLogger.VerboseNewline($"\t\tInitializer function at 0x{initializerPointer:X} is probably NOT the il2cpp initializer - got {registers.Count} register values with keys {string.Join(", ", registers.Keys)}.");
            }

            return (0, 0);
        }

        private (ulong codeReg, ulong metaReg) FindCodeAndMetadataRegDefaultBehavior()
        {
            if (LibCpp2IlMain.MetadataVersion >= 24.2f)
            {
                LibLogger.VerboseNewline("Searching for il2cpp structures in an ELF binary using non-arch-specific method...");
                var searcher = new BinarySearcher(this, LibCpp2IlMain.TheMetadata!.methodDefs.Count(x => x.methodIndex >= 0), LibCpp2IlMain.TheMetadata!.typeDefs.Length);
                
                LibLogger.Verbose("\tLooking for code reg (this might take a while)...");
                var codeReg = searcher.FindCodeRegistrationPost2019();
                LibLogger.VerboseNewline($"Got 0x{codeReg:X}");

                LibLogger.Verbose($"\tLooking for meta reg ({(LibCpp2IlMain.MetadataVersion >= 27f ? "post-27" : "pre-27")})...");
                var metaReg = LibCpp2IlMain.MetadataVersion >= 27f ? searcher.FindMetadataRegistrationPost24_5() : searcher.FindMetadataRegistrationPre24_5();
                LibLogger.VerboseNewline($"Got 0x{metaReg:x}");

                return (codeReg, metaReg);
            }

            throw new Exception("Pre-24.2 support for non-ARM ELF lookup is not yet implemented.");
        }

        public override long RawLength => _raw.Length;

        public override long MapVirtualAddressToRaw(ulong addr)
        {
            var section = _elfProgramHeaderEntries.FirstOrDefault(x => addr >= x.VirtualAddress && addr <= x.VirtualAddress + x.VirtualSize);

            if (section == null)
                throw new InvalidOperationException($"No entry in the Elf PHT contains virtual address 0x{addr:X}");

            return (long) (addr - (section.VirtualAddress - section.RawAddress));
        }

        public override ulong MapRawAddressToVirtual(uint offset)
        {
            if (relocationBlocks.Any(b => b.start <= offset && b.end >= offset))
                throw new InvalidOperationException("Attempt to map a relocation block to a virtual address");
            
            var section = _elfProgramHeaderEntries.First(x => offset >= x.RawAddress && offset < x.RawAddress + x.RawSize);

            return section.VirtualAddress + offset - section.RawAddress;
        }

        public override byte GetByteAtRawAddress(ulong addr) => _raw[addr];

        public override ulong GetRVA(ulong pointer) => (ulong) ((long) pointer - _globalOffset);

        public override byte[] GetRawBinaryContent() => _raw;

        public override ulong GetVirtualAddressOfExportedFunctionByName(string toFind)
        {
            if (!_exportTable.TryGetValue(toFind, out var exportedSymbol))
                return 0;

            return exportedSymbol.VirtualAddress;
        }

        public override ulong GetVirtualAddressOfPrimaryExecutableSection() => _elfSectionHeaderEntries.FirstOrDefault(s => s.Name == ".text")?.VirtualAddress ?? 0;

        public override byte[] GetEntirePrimaryExecutableSection()
        {
            var primarySection = _elfSectionHeaderEntries.FirstOrDefault(s => s.Name == ".text");

            if (primarySection == null)
                return Array.Empty<byte>();

            return GetRawBinaryContent().SubArray((int)primarySection.RawAddress, (int)primarySection.Size);
        }
    }
}