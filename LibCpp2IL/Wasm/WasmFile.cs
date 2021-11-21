using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Wasm
{
    public sealed class WasmFile : Il2CppBinary
    {
        private byte[] _raw;

        internal readonly List<WasmSection> Sections = new();
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
            
            LibLogger.VerboseNewline($"\tAllocated memory block of {_memoryBlock.Bytes.Length} (0x{_memoryBlock.Bytes.Length:X}) bytes ({_memoryBlock.Bytes.Length / 1024 / 1024:F2}MB)");
        }

        public override long RawLength => _raw.Length;
        public override byte GetByteAtRawAddress(ulong addr) => _raw[addr];
        public override byte[] GetRawBinaryContent() => _raw;

        internal WasmDataSection DataSection => (WasmDataSection) Sections.First(s => s.Type == WasmSectionId.SEC_DATA);
        
        public override long MapVirtualAddressToRaw(ulong uiAddr)
        {
            //TODO: This works. But it doesn't account for zero gaps in the file. E.g. windowsRuntimeFactoryCount and the field after are both zero, so they're left out of the data segments and assumed to be zero.
            //TODO: And so trying to just blindly read from the file at the raw address doesn't work. We have to build a map of the virtual memory in ReadClassAtVirtualAddress, and use that.
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