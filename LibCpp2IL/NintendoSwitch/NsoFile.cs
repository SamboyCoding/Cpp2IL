using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Elf;
using LibCpp2IL.Logging;

namespace LibCpp2IL.NintendoSwitch
{
    public sealed class NsoFile : Il2CppBinary
    {
        private const ulong NSO_GLOBAL_OFFSET = 0;
        
        private byte[] _raw;

        private NsoHeader header;
        private NsoModHeader _modHeader;
        private bool isTextCompressed;
        private bool isRoDataCompressed;
        private bool isDataCompressed;
        private List<NsoSegmentHeader> segments = new();
        private List<ElfDynamicEntry> dynamicEntries = new();
        public ElfDynamicSymbol64[] SymbolTable;
        private bool isCompressed => isTextCompressed || isRoDataCompressed || isDataCompressed;


        public NsoFile(MemoryStream input) : base(input)
        {
            _raw = input.GetBuffer();
            is32Bit = false;
            InstructionSetId = DefaultInstructionSets.ARM_V8;

            LibLogger.VerboseNewline("\tReading NSO Early Header...");
            header = new()
            {
                Magic = ReadUInt32(),
                Version = ReadUInt32(),
                Reserved = ReadUInt32(),
                Flags = ReadUInt32()
            };

            if (header.Magic != 0x304F534E)
                throw new($"NSO file should have a magic number of 0x304F534E, got 0x{header.Magic:X}");

            LibLogger.VerboseNewline($"\tOK. Magic number is 0x{header.Magic:X}, version is {header.Version}.");

            isTextCompressed = (header.Flags & 1) != 0;
            isRoDataCompressed = (header.Flags & 2) != 0;
            isDataCompressed = (header.Flags & 4) != 0;

            LibLogger.VerboseNewline($"\tCompression flags: text: {isTextCompressed}, rodata: {isRoDataCompressed}, data: {isDataCompressed}.");

            header.TextSegment = new()
            {
                FileOffset = ReadUInt32(),
                MemoryOffset = ReadUInt32(),
                DecompressedSize = ReadUInt32()
            };
            segments.Add(header.TextSegment);

            LibLogger.VerboseNewline($"\tRead text segment header ok. Reading rodata segment header...");

            header.ModuleOffset = ReadUInt32();
            header.RoDataSegment = new()
            {
                FileOffset = ReadUInt32(),
                MemoryOffset = ReadUInt32(),
                DecompressedSize = ReadUInt32()
            };
            segments.Add(header.RoDataSegment);

            LibLogger.VerboseNewline($"\tRead rodata segment header OK. Reading data segment header...");

            header.ModuleFileSize = ReadUInt32();
            header.DataSegment = new()
            {
                FileOffset = ReadUInt32(),
                MemoryOffset = ReadUInt32(),
                DecompressedSize = ReadUInt32()
            };
            segments.Add(header.DataSegment);

            LibLogger.VerboseNewline($"\tRead data segment OK. Reading post-segment fields...");

            header.BssSize = ReadUInt32();
            header.DigestBuildID = ReadBytes(0x20);
            header.TextCompressedSize = ReadUInt32();
            header.RoDataCompressedSize = ReadUInt32();
            header.DataCompressedSize = ReadUInt32();
            header.NsoHeaderReserved = ReadBytes(0x1C);

            LibLogger.VerboseNewline("\tRead post-segment fields OK. Reading Dynamic section and Api Info offsets...");

            header.APIInfo = new()
            {
                RegionRoDataOffset = ReadUInt32(),
                RegionSize = ReadUInt32()
            };
            header.DynStr = new()
            {
                RegionRoDataOffset = ReadUInt32(),
                RegionSize = ReadUInt32()
            };
            header.DynSym = new()
            {
                RegionRoDataOffset = ReadUInt32(),
                RegionSize = ReadUInt32()
            };

            LibLogger.VerboseNewline($"\tRead offsets OK. Reading hashes...");

            header.TextHash = ReadBytes(0x20);
            header.RoDataHash = ReadBytes(0x20);
            header.DataHash = ReadBytes(0x20);

            LibLogger.VerboseNewline($"\tRead hashes ok.");

            if (!isCompressed)
            {
                ReadModHeader();
                ReadDynamicSection();
                ReadSymbolTable();
                ApplyRelocations();
            }

            LibLogger.VerboseNewline($"\tNSO Read completed OK.");
        }

        private void ReadModHeader()
        {
            LibLogger.VerboseNewline($"\tNSO is decompressed. Reading MOD segment header...");
            
            _modHeader = new();

            //Location of real mod header must be at .text + 4
            //Which allows .text + 0 to be a jump over it
            Position = header.TextSegment.FileOffset + 4;
            _modHeader.ModOffset = ReadUInt32();

            //Now we have the real mod header position, go to it and read
            Position = header.TextSegment.FileOffset + _modHeader.ModOffset + 4;
            _modHeader.DynamicOffset = ReadUInt32() + _modHeader.ModOffset;
            _modHeader.BssStart = ReadUInt32();
            _modHeader.BssEnd = ReadUInt32();
            
            //Construct the bss segment information from the fields we just read
            _modHeader.BssSegment = new()
            {
                FileOffset = _modHeader.BssStart,
                MemoryOffset = _modHeader.BssStart,
                DecompressedSize = _modHeader.BssEnd - _modHeader.BssStart
            };
            
            _modHeader.EhFrameHdrStart = ReadUInt32();
            _modHeader.EhFrameHdrEnd = ReadUInt32();
        }

        private void ReadDynamicSection()
        {
            LibLogger.VerboseNewline($"\tReading NSO Dynamic section...");
            
            Position = MapVirtualAddressToRaw(_modHeader.DynamicOffset);
            
            //This is mostly a sanity check so we don't read the entire damn file 16 bytes at a time
            //This will be way more than we need in general (like, 100 times more)
            var endOfData = header.DataSegment.MemoryOffset + header.DataSegment.DecompressedSize;
            var maxPossibleDynSectionEntryCount = (endOfData - _modHeader.DynamicOffset) / 16; //16 being sizeof(ElfDynamicEntry) on 64-bit

            for (var i = 0; i < maxPossibleDynSectionEntryCount; i++)
            {
                var dynEntry = ReadClassAtRawAddr<ElfDynamicEntry>(-1);
                if (dynEntry.Tag == ElfDynamicType.DT_NULL)
                    //End of dynamic section
                    break;
                dynamicEntries.Add(dynEntry);
            }
        }

        private void ReadSymbolTable()
        {
            LibLogger.Verbose($"\tReading NSO symbol table...");
            
            var hash = GetDynamicEntry(ElfDynamicType.DT_HASH);
            Position = MapVirtualAddressToRaw(hash.Value);
            ReadUInt32(); //Ignored
            var symbolCount = ReadUInt32();

            var symTab = GetDynamicEntry(ElfDynamicType.DT_SYMTAB);
            SymbolTable = ReadClassArrayAtVirtualAddress<ElfDynamicSymbol64>((ulong) MapVirtualAddressToRaw(symTab.Value), symbolCount);
            
            LibLogger.VerboseNewline($"Got {SymbolTable.Length} symbols");
        }

        private void ApplyRelocations()
        {
            ElfRelaEntry[] relaEntries;

            try
            {
                var dtRela = GetDynamicEntry(ElfDynamicType.DT_RELA);
                var dtRelaSize = GetDynamicEntry(ElfDynamicType.DT_RELASZ);
                relaEntries = ReadClassArrayAtVirtualAddress<ElfRelaEntry>(dtRela.Value, (long) (dtRelaSize.Value / 24)); //24 being sizeof(ElfRelaEntry) on 64-bit
            }
            catch
            {
                //If we don't have relocations, that's fine.
                return;
            }
            
            LibLogger.VerboseNewline($"\tApplying {relaEntries.Length} relocations from DT_RELA...");
            
            foreach (var elfRelaEntry in relaEntries)
            {
                switch (elfRelaEntry.Type)
                {
                    case ElfRelocationType.R_AARCH64_ABS64:
                    case ElfRelocationType.R_AARCH64_GLOB_DAT:
                        var symbol = SymbolTable[elfRelaEntry.Symbol];
                        WriteWord((int) MapVirtualAddressToRaw(elfRelaEntry.Offset), symbol.Value + elfRelaEntry.Addend);
                        break;
                    case ElfRelocationType.R_AARCH64_RELATIVE:
                        WriteWord((int) MapVirtualAddressToRaw(elfRelaEntry.Offset), elfRelaEntry.Addend);
                        break;
                    default:
                        LibLogger.WarnNewline($"Unknown relocation type {elfRelaEntry.Type}");
                        break;
                }
            }
        }
        
        public ElfDynamicEntry GetDynamicEntry(ElfDynamicType tag) => dynamicEntries.Find(x => x.Tag == tag);

        public NsoFile Decompress()
        {
            if (!isCompressed)
                return this;
            
            LibLogger.InfoNewline("\tDecompressing NSO file...");

            var unCompressedStream = new MemoryStream();
            var writer = new BinaryWriter(unCompressedStream);
            writer.Write(header.Magic);
            writer.Write(header.Version);
            writer.Write(header.Reserved);
            writer.Write(0); //Flags
            writer.Write(header.TextSegment.FileOffset);
            writer.Write(header.TextSegment.MemoryOffset);
            writer.Write(header.TextSegment.DecompressedSize);
            writer.Write(header.ModuleOffset);
            var roOffset = header.TextSegment.FileOffset + header.TextSegment.DecompressedSize;
            writer.Write(roOffset); //header.RoDataSegment.FileOffset
            writer.Write(header.RoDataSegment.MemoryOffset);
            writer.Write(header.RoDataSegment.DecompressedSize);
            writer.Write(header.ModuleFileSize);
            writer.Write(roOffset + header.RoDataSegment.DecompressedSize); //header.DataSegment.FileOffset
            writer.Write(header.DataSegment.MemoryOffset);
            writer.Write(header.DataSegment.DecompressedSize);
            writer.Write(header.BssSize);
            writer.Write(header.DigestBuildID);
            writer.Write(header.TextCompressedSize);
            writer.Write(header.RoDataCompressedSize);
            writer.Write(header.DataCompressedSize);
            writer.Write(header.NsoHeaderReserved);
            writer.Write(header.APIInfo.RegionRoDataOffset);
            writer.Write(header.APIInfo.RegionSize);
            writer.Write(header.DynStr.RegionRoDataOffset);
            writer.Write(header.DynStr.RegionSize);
            writer.Write(header.DynSym.RegionRoDataOffset);
            writer.Write(header.DynSym.RegionSize);
            writer.Write(header.TextHash);
            writer.Write(header.RoDataHash);
            writer.Write(header.DataHash);
            writer.BaseStream.Position = header.TextSegment.FileOffset;
            Position = header.TextSegment.FileOffset;
            var textBytes = ReadBytes((int)header.TextCompressedSize);
            if (isTextCompressed)
            {
                var unCompressedData = new byte[header.TextSegment.DecompressedSize];
                using (var decoder = new Lz4DecodeStream(new MemoryStream(textBytes)))
                {
                    decoder.Read(unCompressedData, 0, unCompressedData.Length);
                }

                writer.Write(unCompressedData);
            }
            else
            {
                writer.Write(textBytes);
            }

            var roDataBytes = ReadBytes((int)header.RoDataCompressedSize);
            if (isRoDataCompressed)
            {
                var unCompressedData = new byte[header.RoDataSegment.DecompressedSize];
                using (var decoder = new Lz4DecodeStream(new MemoryStream(roDataBytes)))
                {
                    decoder.Read(unCompressedData, 0, unCompressedData.Length);
                }

                writer.Write(unCompressedData);
            }
            else
            {
                writer.Write(roDataBytes);
            }

            var dataBytes = ReadBytes((int)header.DataCompressedSize);
            if (isDataCompressed)
            {
                var unCompressedData = new byte[header.DataSegment.DecompressedSize];
                using (var decoder = new Lz4DecodeStream(new MemoryStream(dataBytes)))
                {
                    decoder.Read(unCompressedData, 0, unCompressedData.Length);
                }

                writer.Write(unCompressedData);
            }
            else
            {
                writer.Write(dataBytes);
            }

            writer.Flush();
            unCompressedStream.Position = 0;
            return new(unCompressedStream);
        }

        public override long RawLength => _raw.Length;
        public override byte GetByteAtRawAddress(ulong addr) => _raw[addr];

        public override long MapVirtualAddressToRaw(ulong addr)
        {
            var segment = segments.FirstOrDefault(x => addr - NSO_GLOBAL_OFFSET >= x.MemoryOffset && addr - NSO_GLOBAL_OFFSET <= x.MemoryOffset + x.DecompressedSize);
            if (segment == null)
                throw new InvalidOperationException($"NSO: Address 0x{addr:X} is not present in any of the segments. Known segment ends are (hex) {string.Join(", ", segments.Select(s => (s.MemoryOffset + s.DecompressedSize).ToString("X")))}");
            
            return (long)(addr - (segment.MemoryOffset + NSO_GLOBAL_OFFSET) + segment.FileOffset);
        }

        public override ulong MapRawAddressToVirtual(uint offset)
        {
            var segment = segments.FirstOrDefault(x => offset >= x.FileOffset && offset <= x.FileOffset + x.DecompressedSize);
            if (segment == null)
            {
                return 0;
            }
            return offset - segment.FileOffset + (NSO_GLOBAL_OFFSET + segment.MemoryOffset);
        }

        public override ulong GetRVA(ulong pointer)
        {
            return pointer;
        }

        public override byte[] GetRawBinaryContent() => _raw;

        public override ulong GetVirtualAddressOfExportedFunctionByName(string toFind)
        {
            return 0;
        }

        public override byte[] GetEntirePrimaryExecutableSection()
        {
            return _raw.Skip((int)header.TextSegment.FileOffset).Take((int)header.TextSegment.DecompressedSize).ToArray();
        }

        public override ulong GetVirtualAddressOfPrimaryExecutableSection()
        {
            return header.TextSegment.MemoryOffset;
        }
    }
}