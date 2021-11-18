using System;
using System.IO;
using System.Linq;
using System.Text;
using LibCpp2IL.Logging;

namespace LibCpp2IL.PE
{
    public sealed class PE : Il2CppBinary
    {
        //Initialized in constructor
        internal readonly byte[] raw; //Internal for PlusSearch
        
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

        //Pointers

        public PE(MemoryStream input, long maxMetadataUsages) : base(input, maxMetadataUsages)
        {
            raw = input.GetBuffer();
            LibLogger.Verbose("\tReading PE File Header...");
            var start = DateTime.Now;

            if (ReadUInt16() != 0x5A4D) //Magic number
                throw new FormatException("ERROR: Magic number mismatch.");
            Position = 0x3C; //Signature position position (lol)
            Position = ReadUInt32(); //Signature position
            if (ReadUInt32() != 0x00004550) //Signature
                throw new FormatException("ERROR: Invalid PE file signature");

            var fileHeader = ReadClassAtRawAddr<FileHeader>(-1);
            if (fileHeader.Machine == 0x014c) //Intel 386
            {
                is32Bit = true;
                InstructionSet = InstructionSet.X86_32;
                peOptionalHeader32 = ReadClassAtRawAddr<OptionalHeader>(-1);
                peOptionalHeader32.DataDirectory = ReadClassArrayAtRawAddr<DataDirectory>(-1, peOptionalHeader32.NumberOfRvaAndSizes);
                peImageBase = peOptionalHeader32.ImageBase;
            }
            else if (fileHeader.Machine == 0x8664) //AMD64
            {
                InstructionSet = InstructionSet.X86_64;
                peOptionalHeader64 = ReadClassAtRawAddr<OptionalHeader64>(-1);
                peOptionalHeader64.DataDirectory = ReadClassArrayAtRawAddr<DataDirectory>(-1, peOptionalHeader64.NumberOfRvaAndSizes);
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

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            LibLogger.VerboseNewline($"\t\tImage Base at 0x{peImageBase:X}");
            LibLogger.VerboseNewline($"\t\tDLL is {(is32Bit ? "32" : "64")}-bit");
        }
#pragma warning restore 8618

        public override long MapVirtualAddressToRaw(ulong uiAddr)
        {
            if(uiAddr < peImageBase)
                throw new OverflowException($"Provided address, 0x{uiAddr:X}, was less than image base, 0x{peImageBase:X}");
            
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

        public override ulong MapRawAddressToVirtual(uint offset)
        {
            var section = peSectionHeaders.First(x => offset >= x.PointerToRawData && offset < x.PointerToRawData + x.SizeOfRawData);

            return peImageBase + section.VirtualAddress + offset - section.PointerToRawData;
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

            try
            {
                //Non-virtual addresses for these
                var directoryEntryExports = ReadClassAtVirtualAddress<PeDirectoryEntryExport>(addrExportTable + peImageBase);

                peExportedFunctionPointers = ReadClassArrayAtVirtualAddress<uint>(directoryEntryExports.RawAddressOfExportTable + peImageBase, directoryEntryExports.NumberOfExports);
                peExportedFunctionNamePtrs = ReadClassArrayAtVirtualAddress<uint>(directoryEntryExports.RawAddressOfExportNameTable + peImageBase, directoryEntryExports.NumberOfExportNames);
                peExportedFunctionOrdinals = ReadClassArrayAtVirtualAddress<ushort>(directoryEntryExports.RawAddressOfExportOrdinalTable + peImageBase, directoryEntryExports.NumberOfExportNames); //This uses the name count per MSoft spec
            }
            catch (EndOfStreamException)
            {
                LibLogger.WarnNewline($"PE does not appear to contain a valid export table! It would be apparently located at virt address 0x{addrExportTable + peImageBase:X}, raw 0x{MapVirtualAddressToRaw(addrExportTable + peImageBase):X}, but that's beyond the end of the binary. No exported functions will be accessible.");
                peExportedFunctionPointers = Array.Empty<uint>();
                peExportedFunctionNamePtrs = Array.Empty<uint>();
                peExportedFunctionOrdinals = Array.Empty<ushort>();
            }
        }

        public override ulong GetVirtualAddressOfExportedFunctionByName(string toFind)
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

        public override ulong GetRVA(ulong pointer)
        {
            return pointer - peImageBase;
        }
        
        public override byte[] GetEntirePrimaryExecutableSection()
        {
            var primarySection = peSectionHeaders.FirstOrDefault(s => s.Name == ".text");

            if (primarySection == null)
                return Array.Empty<byte>();

            return GetRawBinaryContent().SubArray((int)primarySection.PointerToRawData, (int)primarySection.SizeOfRawData);
        }
        
        public override ulong GetVirtualAddressOfPrimaryExecutableSection() => peSectionHeaders.FirstOrDefault(s => s.Name == ".text")?.VirtualAddress + peImageBase ?? 0;

        public override byte GetByteAtRawAddress(ulong addr) => raw[addr];

        public override long RawLength => raw.Length;

        public override byte[] GetRawBinaryContent() => raw;
    }
}