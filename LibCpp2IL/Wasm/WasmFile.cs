using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Wasm
{
    public sealed class WasmFile : Il2CppBinary
    {
        public readonly List<WasmFunctionDefinition> FunctionTable = new();
        
        internal readonly List<WasmSection> Sections = new();
        
        private byte[] _raw;
        private WasmMemoryBlock _memoryBlock;

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
                if(importSectionEntry.Kind == WasmImportEntry.WasmExternalKind.EXT_FUNCTION)
                    FunctionTable.Add(new(importSectionEntry));
            }

            for (var index = 0; index < CodeSection.Functions.Count; index++)
            {
                var codeSectionFunction = CodeSection.Functions[index];
                FunctionTable.Add(new(this, codeSectionFunction, index));
            }
            
            LibLogger.VerboseNewline($"\tBuilt function table of {FunctionTable.Count} entries.");
        }

        public override long RawLength => _raw.Length;
        public override byte GetByteAtRawAddress(ulong addr) => _raw[addr];
        public override byte[] GetRawBinaryContent() => _raw;

        public WasmFunctionDefinition GetFunctionWithIndex(int index)
        {
            var realIndex = ElementSection.Elements[0].FunctionIndices![index];
            return FunctionTable[(int) realIndex];
        }

        internal WasmTypeSection TypeSection => (WasmTypeSection) Sections.First(s => s.Type == WasmSectionId.SEC_TYPE);
        
        internal WasmFunctionSection FunctionSection => (WasmFunctionSection) Sections.First(s => s.Type == WasmSectionId.SEC_FUNCTION);
        
        internal WasmDataSection DataSection => (WasmDataSection) Sections.First(s => s.Type == WasmSectionId.SEC_DATA);
        internal WasmCodeSection CodeSection => (WasmCodeSection) Sections.First(s => s.Type == WasmSectionId.SEC_CODE);
        internal WasmImportSection ImportSection => (WasmImportSection) Sections.First(s => s.Type == WasmSectionId.SEC_IMPORT);
        internal WasmElementSection ElementSection => (WasmElementSection) Sections.First(s => s.Type == WasmSectionId.SEC_ELEMENT);
        
        public override long MapVirtualAddressToRaw(ulong uiAddr)
        {
            // var data = DataSection;
            // var matchingEntry = data.DataEntries.FirstOrDefault(e => e.VirtualOffset <= uiAddr && e.VirtualOffset + (ulong) e.Data.Length > uiAddr);
            // if (matchingEntry != null)
            // {
            //     return matchingEntry.FileOffset + (long) (uiAddr - (ulong) matchingEntry.VirtualOffset);
            // }
            
            return (long) uiAddr;
        }

        public override ulong MapRawAddressToVirtual(uint offset)
        {
            var data = DataSection;
            if (offset > data.Pointer && offset < data.Pointer + (long) data.Size)
            {
                var segment = data.DataEntries.FirstOrDefault(entry => offset > entry.FileOffset && offset < entry.FileOffset + entry.Data.Length);
            
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
            throw new System.NotImplementedException();
        }

        public override byte[] GetEntirePrimaryExecutableSection() => ((WasmCodeSection) Sections.First(s => s.Type == WasmSectionId.SEC_CODE)).RawSectionContent;

        public override ulong GetVirtualAddressOfPrimaryExecutableSection() => (ulong) Sections.First(s => s.Type == WasmSectionId.SEC_CODE).Pointer;
    }
}