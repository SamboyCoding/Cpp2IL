using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Logging;
using WasmDisassembler;

namespace LibCpp2IL.Wasm
{
    public sealed class WasmFile : Il2CppBinary
    {
        public readonly List<WasmFunctionDefinition> FunctionTable = new();
        
        internal readonly List<WasmSection> Sections = new();
        
        private byte[] _raw;
        private WasmMemoryBlock _memoryBlock;
        private readonly Dictionary<string, WasmDynCallCoefficients> DynCallCoefficients = new();

        public WasmFile(MemoryStream input, long maxMetadataUsages) : base(input, maxMetadataUsages)
        {
            is32Bit = true;
            InstructionSet = InstructionSet.WASM;
            _raw = input.GetBuffer();
            var magic = ReadUInt32();
            var version = ReadInt32();

            if (magic != 0x6D736100) //\0asm
                throw new Exception($"WASM magic mismatch; got 0x{magic:X}");

            if (version != 1)
                throw new Exception($"Unknown version, expecting 1, got {version}");

            LibLogger.VerboseNewline("\tWASM Magic and version match expectations. Reading sections...");

            var sanityCount = 0;
            while(Position < RawLength && sanityCount < 1000)
            {
                var section = WasmSection.MakeSection(this);
                Position = section.Pointer + (long) section.Size;
                Sections.Add(section);
                sanityCount++;
            }
            
            LibLogger.VerboseNewline($"\tRead {Sections.Count} WASM sections. Allocating memory block...");

            _memoryBlock = new(this);
            
            LibLogger.VerboseNewline($"\tAllocated memory block of {_memoryBlock.Bytes.Length} (0x{_memoryBlock.Bytes.Length:X}) bytes ({_memoryBlock.Bytes.Length / 1024 / 1024:F2}MB). Constructing function table...");
            
            foreach (var importSectionEntry in ImportSection.Entries)
            {
                if(importSectionEntry.Kind == WasmExternalKind.EXT_FUNCTION)
                    FunctionTable.Add(new(importSectionEntry));
            }

            for (var index = 0; index < CodeSection.Functions.Count; index++)
            {
                var codeSectionFunction = CodeSection.Functions[index];
                FunctionTable.Add(new(this, codeSectionFunction, index));
            }
            
            LibLogger.VerboseNewline($"\tBuilt function table of {FunctionTable.Count} entries. Calculating dynCall coefficients...");
            
            CalculateDynCallOffsets();
            
            LibLogger.VerboseNewline($"\tGot dynCall coefficients for {DynCallCoefficients.Count} signatures");
        }

        public override long RawLength => _raw.Length;
        public override byte GetByteAtRawAddress(ulong addr) => _raw[addr];
        public override byte[] GetRawBinaryContent() => _raw;

        public WasmFunctionDefinition GetFunctionFromIndexAndSignature(ulong index, string signature)
        {
            if (!DynCallCoefficients.TryGetValue(signature, out var coefficients))
                throw new($"Can't get function with signature {signature}, as it's not defined in the binary");

            //Calculate adjusted dyncall index
            index = (index & coefficients.andWith) + coefficients.addConstant;

            //Use element section to look up real index
            var realIndex = ElementSection.Elements[0].FunctionIndices![(int) index];
            
            //Look up real index in function table
            return FunctionTable[(int) realIndex];
        }

        internal WasmTypeSection TypeSection => (WasmTypeSection) Sections.First(s => s.Type == WasmSectionId.SEC_TYPE);
        
        internal WasmFunctionSection FunctionSection => (WasmFunctionSection) Sections.First(s => s.Type == WasmSectionId.SEC_FUNCTION);
        
        internal WasmDataSection DataSection => (WasmDataSection) Sections.First(s => s.Type == WasmSectionId.SEC_DATA);
        internal WasmCodeSection CodeSection => (WasmCodeSection) Sections.First(s => s.Type == WasmSectionId.SEC_CODE);
        internal WasmImportSection ImportSection => (WasmImportSection) Sections.First(s => s.Type == WasmSectionId.SEC_IMPORT);
        internal WasmElementSection ElementSection => (WasmElementSection) Sections.First(s => s.Type == WasmSectionId.SEC_ELEMENT);
        internal WasmExportSection ExportSection => (WasmExportSection) Sections.First(s => s.Type == WasmSectionId.SEC_EXPORT);

        private void CalculateDynCallOffsets()
        {
            var codeSec = CodeSection;
            foreach (var exportedDynCall in ExportSection.Exports.Where(e => e.Kind == WasmExternalKind.EXT_FUNCTION && e.Name.Value.StartsWith("dynCall_")))
            {
                var signature = exportedDynCall.Name.Value["dynCall_".Length..];

                var function = FunctionTable[(int) exportedDynCall.Index].AssociatedFunctionBody;
                var funcBody = function!.Instructions;
                var disassembled = Disassembler.Disassemble(funcBody, (uint) function.InstructionsOffset);
                
                //Find the consts, ands, and adds
                var relevantInstructions = disassembled.Where(i => i.Mnemonic is WasmMnemonic.I32Const or WasmMnemonic.I32And or WasmMnemonic.I32Add).ToArray();

                ulong andWith;
                ulong add;

                if (relevantInstructions.Length == 2)
                {
                    if (relevantInstructions[^1].Mnemonic == WasmMnemonic.I32And)
                    {
                        andWith = (ulong) relevantInstructions[0].Operands[0];
                        add = 0;
                    } else if (relevantInstructions[^1].Mnemonic == WasmMnemonic.I32Add)
                    {
                        add = (ulong) relevantInstructions[0].Operands[0];
                        andWith = int.MaxValue;
                    }
                    else
                    {
                        LibLogger.WarnNewline($"\t\tCouldn't calculate coefficients for {signature}, got only 2 instructions but the last was {relevantInstructions[^1].Mnemonic}, not I32And or I32Add");
                        continue;
                    }
                } else if (relevantInstructions.Length == 4)
                {
                    //Should be const, and, const, add
                    if (!relevantInstructions.Select(i => i.Mnemonic).SequenceEqual(new[] {WasmMnemonic.I32Const, WasmMnemonic.I32And, WasmMnemonic.I32Const, WasmMnemonic.I32Add}))
                    {
                        LibLogger.WarnNewline($"\t\tCouldn't calculate coefficients for {signature}, got mnemonics {string.Join(", ", relevantInstructions.Select(i => i.Mnemonic))}, expecting I32Const, I32And, I32Const, I32Add");
                        continue;
                    }
                    
                    andWith = (ulong) relevantInstructions[0].Operands[0];
                    add = (ulong) relevantInstructions[2].Operands[0];
                }
                else
                {
                    LibLogger.WarnNewline($"\t\tCouldn't calculate coefficients for {signature}, got {relevantInstructions.Length} instructions; expecting 4");
                    continue;
                }

                DynCallCoefficients[signature] = new()
                {
                    andWith = andWith,
                    addConstant = add,
                };
            }
        }
        
        public override long MapVirtualAddressToRaw(ulong uiAddr)
        {
            // var data = DataSection;
            // var matchingEntry = data.DataEntries.FirstOrDefault(e => e.VirtualOffset <= uiAddr && e.VirtualOffset + (ulong) e.Data.Length > uiAddr);
            // if (matchingEntry != null)
            // {
            //     return matchingEntry.FileOffset + (long) (uiAddr - (ulong) matchingEntry.VirtualOffset);
            // }

            if (uiAddr > (ulong) (_memoryBlock.Bytes.Length + _raw.Length))
                throw new("Way out of bounds");
            
            return (long) uiAddr;
        }

        public override ulong MapRawAddressToVirtual(uint offset)
        {
            var data = DataSection;
            if (offset > data.Pointer && offset < data.Pointer + (long) data.Size)
            {
                var segment = data.DataEntries.FirstOrDefault(entry => offset >= entry.FileOffset && offset < entry.FileOffset + entry.Data.Length);
            
                if (segment is {VirtualOffset: < ulong.MaxValue})
                {
                    return segment.VirtualOffset + (ulong) (offset - segment.FileOffset);
                }
            }
            
            return offset;
        }

        public override Stream BaseStream => _memoryBlock?.BaseStream ?? base.BaseStream;

        //Delegate to the memory block
        internal override object? ReadPrimitive(Type type, bool overrideArchCheck = false) => _memoryBlock.ReadPrimitive(type, overrideArchCheck);

        public override string ReadStringToNull(long offset) => _memoryBlock.ReadStringToNull(offset);

        public override ulong GetRVA(ulong pointer)
        {
            return pointer;
        }

        public override ulong GetVirtualAddressOfExportedFunctionByName(string toFind)
        {
            return 0; //Never going to be anything useful, so don't bother looking
        }

        public override byte[] GetEntirePrimaryExecutableSection() => ((WasmCodeSection) Sections.First(s => s.Type == WasmSectionId.SEC_CODE)).RawSectionContent;

        public override ulong GetVirtualAddressOfPrimaryExecutableSection() => (ulong) Sections.First(s => s.Type == WasmSectionId.SEC_CODE).Pointer;
    }
}