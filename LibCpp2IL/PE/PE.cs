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
            Console.Write("Reading PE File Header...");
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

        public bool PlusSearch(int methodCount, int typeDefinitionsCount)
        {
            ulong pCodeRegistration = 0;
            ulong pMetadataRegistration;

            Console.WriteLine("Attempting to locate code and metadata registration functions...");

            var plusSearch = new BinarySearcher(this, methodCount, typeDefinitionsCount);

            Console.WriteLine("\t-Searching for MetadataReg...");
            
            pMetadataRegistration = LibCpp2IlMain.MetadataVersion < 27f 
                ? plusSearch.FindMetadataRegistrationPre27() 
                : plusSearch.FindMetadataRegistrationPost27();

            Console.WriteLine("\t-Searching for CodeReg...");

            if (pCodeRegistration == 0)
            {
                if (LibCpp2IlMain.MetadataVersion >= 24.2f)
                {
                    Console.WriteLine("\t\tUsing mscorlib full-disassembly approach to get codereg, this may take a while...");
                    pCodeRegistration = plusSearch.FindCodeRegistrationPost2019();
                }
                else
                    pCodeRegistration = plusSearch.FindCodeRegistrationPre2019();
            }

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

        public override ulong GetRVA(ulong pointer)
        {
            return pointer - peImageBase;
        }

        public InstructionList DisassembleTextSection()
        {
            var textSection = peSectionHeaders.First(s => s.Name == ".text");
            var toDisasm = raw.SubArray((int) textSection.PointerToRawData, (int) textSection.SizeOfRawData);
            return LibCpp2ILUtils.DisassembleBytesNew(is32Bit, toDisasm, textSection.VirtualAddress + peImageBase);
        }

        public override byte GetByteAtRawAddress(ulong addr) => raw[addr];

        public override long RawLength => raw.Length;

        public override byte[] GetRawBinaryContent() => raw;
    }
}