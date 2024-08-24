using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using LibCpp2IL.Elf;
using LibCpp2IL.Logging;

namespace LibCpp2IL.NintendoSwitch;

public sealed class NsoFile : Il2CppBinary
{
    private const ulong NsoGlobalOffset = 0;

    private readonly byte[] _raw;

    private readonly NsoHeader _header;
    private readonly bool _isTextCompressed;
    private readonly bool _isRoDataCompressed;
    private readonly bool _isDataCompressed;

    private NsoModHeader _modHeader = null!;
    private ElfDynamicSymbol64[] _symbolTable = null!;
    private List<NsoSegmentHeader> segments = [];
    private List<ElfDynamicEntry> dynamicEntries = [];

    private bool IsCompressed => _isTextCompressed || _isRoDataCompressed || _isDataCompressed;

    public NsoFile(MemoryStream input) : base(input)
    {
        _raw = input.GetBuffer();
        is32Bit = false;
        InstructionSetId = DefaultInstructionSets.ARM_V8;

        LibLogger.VerboseNewline("\tReading NSO Early Header...");
        _header = new() { Magic = ReadUInt32(), Version = ReadUInt32(), Reserved = ReadUInt32(), Flags = ReadUInt32() };

        if (_header.Magic != 0x304F534E)
            throw new($"NSO file should have a magic number of 0x304F534E, got 0x{_header.Magic:X}");

        LibLogger.VerboseNewline($"\tOK. Magic number is 0x{_header.Magic:X}, version is {_header.Version}.");

        _isTextCompressed = (_header.Flags & 1) != 0;
        _isRoDataCompressed = (_header.Flags & 2) != 0;
        _isDataCompressed = (_header.Flags & 4) != 0;

        LibLogger.VerboseNewline($"\tCompression flags: text: {_isTextCompressed}, rodata: {_isRoDataCompressed}, data: {_isDataCompressed}.");

        _header.TextSegment = new() { FileOffset = ReadUInt32(), MemoryOffset = ReadUInt32(), DecompressedSize = ReadUInt32() };
        segments.Add(_header.TextSegment);

        LibLogger.VerboseNewline($"\tRead text segment header ok. Reading rodata segment header...");

        _header.ModuleOffset = ReadUInt32();
        _header.RoDataSegment = new() { FileOffset = ReadUInt32(), MemoryOffset = ReadUInt32(), DecompressedSize = ReadUInt32() };
        segments.Add(_header.RoDataSegment);

        LibLogger.VerboseNewline($"\tRead rodata segment header OK. Reading data segment header...");

        _header.ModuleFileSize = ReadUInt32();
        _header.DataSegment = new() { FileOffset = ReadUInt32(), MemoryOffset = ReadUInt32(), DecompressedSize = ReadUInt32() };
        segments.Add(_header.DataSegment);

        LibLogger.VerboseNewline($"\tRead data segment OK. Reading post-segment fields...");

        _header.BssSize = ReadUInt32();
        _header.DigestBuildId = ReadBytes(0x20);
        _header.TextCompressedSize = ReadUInt32();
        _header.RoDataCompressedSize = ReadUInt32();
        _header.DataCompressedSize = ReadUInt32();
        _header.NsoHeaderReserved = ReadBytes(0x1C);

        LibLogger.VerboseNewline("\tRead post-segment fields OK. Reading Dynamic section and Api Info offsets...");

        _header.ApiInfo = new() { RegionRoDataOffset = ReadUInt32(), RegionSize = ReadUInt32() };
        _header.DynStr = new() { RegionRoDataOffset = ReadUInt32(), RegionSize = ReadUInt32() };
        _header.DynSym = new() { RegionRoDataOffset = ReadUInt32(), RegionSize = ReadUInt32() };

        LibLogger.VerboseNewline($"\tRead offsets OK. Reading hashes...");

        _header.TextHash = ReadBytes(0x20);
        _header.RoDataHash = ReadBytes(0x20);
        _header.DataHash = ReadBytes(0x20);

        LibLogger.VerboseNewline($"\tRead hashes ok.");

        if (!IsCompressed)
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
        Position = _header.TextSegment.FileOffset + 4;
        _modHeader.ModOffset = ReadUInt32();

        //Now we have the real mod header position, go to it and read
        Position = _header.TextSegment.FileOffset + _modHeader.ModOffset + 4;
        _modHeader.DynamicOffset = ReadUInt32() + _modHeader.ModOffset;
        _modHeader.BssStart = ReadUInt32();
        _modHeader.BssEnd = ReadUInt32();

        //Construct the bss segment information from the fields we just read
        _modHeader.BssSegment = new() { FileOffset = _modHeader.BssStart, MemoryOffset = _modHeader.BssStart, DecompressedSize = _modHeader.BssEnd - _modHeader.BssStart };

        _modHeader.EhFrameHdrStart = ReadUInt32();
        _modHeader.EhFrameHdrEnd = ReadUInt32();
    }

    private void ReadDynamicSection()
    {
        LibLogger.VerboseNewline($"\tReading NSO Dynamic section...");

        Position = MapVirtualAddressToRaw(_modHeader.DynamicOffset);

        //This is mostly a sanity check so we don't read the entire damn file 16 bytes at a time
        //This will be way more than we need in general (like, 100 times more)
        var endOfData = _header.DataSegment.MemoryOffset + _header.DataSegment.DecompressedSize;
        var maxPossibleDynSectionEntryCount = (endOfData - _modHeader.DynamicOffset) / 16; //16 being sizeof(ElfDynamicEntry) on 64-bit

        for (var i = 0; i < maxPossibleDynSectionEntryCount; i++)
        {
            var dynEntry = ReadReadable<ElfDynamicEntry>();
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

        if (hash == null)
        {
            LibLogger.WarnNewline("\tNo DT_HASH found in NSO, symbols will not be resolved");
            return;
        }

        Position = MapVirtualAddressToRaw(hash.Value);
        ReadUInt32(); //Ignored
        var symbolCount = ReadUInt32();

        var symTab = GetDynamicEntry(ElfDynamicType.DT_SYMTAB);

        if (symTab == null)
        {
            LibLogger.WarnNewline("\tNo DT_SYMTAB found in NSO, symbols will not be resolved");
            return;
        }

        _symbolTable = ReadReadableArrayAtVirtualAddress<ElfDynamicSymbol64>((ulong)MapVirtualAddressToRaw(symTab.Value), symbolCount);

        LibLogger.VerboseNewline($"Got {_symbolTable.Length} symbols");
    }

    private void ApplyRelocations()
    {
        ElfRelaEntry[] relaEntries;

        try
        {
            var dtRela = GetDynamicEntry(ElfDynamicType.DT_RELA) ?? throw new("No relocations found in NSO");
            var dtRelaSize = GetDynamicEntry(ElfDynamicType.DT_RELASZ) ?? throw new("No relocation size entry found in NSO");
            relaEntries = ReadReadableArrayAtVirtualAddress<ElfRelaEntry>(dtRela.Value, (long)(dtRelaSize.Value / 24)); //24 being sizeof(ElfRelaEntry) on 64-bit
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
                    var symbol = _symbolTable[elfRelaEntry.Symbol];
                    WriteWord((int)MapVirtualAddressToRaw(elfRelaEntry.Offset), symbol.Value + elfRelaEntry.Addend);
                    break;
                case ElfRelocationType.R_AARCH64_RELATIVE:
                    WriteWord((int)MapVirtualAddressToRaw(elfRelaEntry.Offset), elfRelaEntry.Addend);
                    break;
                default:
                    LibLogger.WarnNewline($"Unknown relocation type {elfRelaEntry.Type}");
                    break;
            }
        }
    }

    public ElfDynamicEntry? GetDynamicEntry(ElfDynamicType tag) => dynamicEntries.Find(x => x.Tag == tag);

    [SuppressMessage("ReSharper", "MustUseReturnValue")]
    public NsoFile Decompress()
    {
        if (!IsCompressed)
            return this;

        LibLogger.InfoNewline("\tDecompressing NSO file...");

        var unCompressedStream = new MemoryStream();
        var writer = new BinaryWriter(unCompressedStream);
        writer.Write(_header.Magic);
        writer.Write(_header.Version);
        writer.Write(_header.Reserved);
        writer.Write(0); //Flags
        writer.Write(_header.TextSegment.FileOffset);
        writer.Write(_header.TextSegment.MemoryOffset);
        writer.Write(_header.TextSegment.DecompressedSize);
        writer.Write(_header.ModuleOffset);
        var roOffset = _header.TextSegment.FileOffset + _header.TextSegment.DecompressedSize;
        writer.Write(roOffset); //header.RoDataSegment.FileOffset
        writer.Write(_header.RoDataSegment.MemoryOffset);
        writer.Write(_header.RoDataSegment.DecompressedSize);
        writer.Write(_header.ModuleFileSize);
        writer.Write(roOffset + _header.RoDataSegment.DecompressedSize); //header.DataSegment.FileOffset
        writer.Write(_header.DataSegment.MemoryOffset);
        writer.Write(_header.DataSegment.DecompressedSize);
        writer.Write(_header.BssSize);
        writer.Write(_header.DigestBuildId);
        writer.Write(_header.TextCompressedSize);
        writer.Write(_header.RoDataCompressedSize);
        writer.Write(_header.DataCompressedSize);
        writer.Write(_header.NsoHeaderReserved);
        writer.Write(_header.ApiInfo.RegionRoDataOffset);
        writer.Write(_header.ApiInfo.RegionSize);
        writer.Write(_header.DynStr.RegionRoDataOffset);
        writer.Write(_header.DynStr.RegionSize);
        writer.Write(_header.DynSym.RegionRoDataOffset);
        writer.Write(_header.DynSym.RegionSize);
        writer.Write(_header.TextHash);
        writer.Write(_header.RoDataHash);
        writer.Write(_header.DataHash);
        writer.BaseStream.Position = _header.TextSegment.FileOffset;
        Position = _header.TextSegment.FileOffset;
        var textBytes = ReadBytes((int)_header.TextCompressedSize);
        if (_isTextCompressed)
        {
            var unCompressedData = new byte[_header.TextSegment.DecompressedSize];
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

        var roDataBytes = ReadBytes((int)_header.RoDataCompressedSize);
        if (_isRoDataCompressed)
        {
            var unCompressedData = new byte[_header.RoDataSegment.DecompressedSize];
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

        var dataBytes = ReadBytes((int)_header.DataCompressedSize);
        if (_isDataCompressed)
        {
            var unCompressedData = new byte[_header.DataSegment.DecompressedSize];
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

    public override long MapVirtualAddressToRaw(ulong addr, bool throwOnError = true)
    {
        var segment = segments.FirstOrDefault(x => addr - NsoGlobalOffset >= x.MemoryOffset && addr - NsoGlobalOffset <= x.MemoryOffset + x.DecompressedSize);
        if (segment == null)
            if (throwOnError)
                throw new InvalidOperationException($"NSO: Address 0x{addr:X} is not present in any of the segments. Known segment ends are (hex) {string.Join(", ", segments.Select(s => (s.MemoryOffset + s.DecompressedSize).ToString("X")))}");
            else
                return VirtToRawInvalidNoMatch;

        return (long)(addr - (segment.MemoryOffset + NsoGlobalOffset) + segment.FileOffset);
    }

    public override ulong MapRawAddressToVirtual(uint offset)
    {
        var segment = segments.FirstOrDefault(x => offset >= x.FileOffset && offset <= x.FileOffset + x.DecompressedSize);
        if (segment == null)
        {
            return 0;
        }

        return offset - segment.FileOffset + (NsoGlobalOffset + segment.MemoryOffset);
    }

    public override ulong GetRva(ulong pointer)
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
        return _raw.Skip((int)_header.TextSegment.FileOffset).Take((int)_header.TextSegment.DecompressedSize).ToArray();
    }

    public override ulong GetVirtualAddressOfPrimaryExecutableSection()
    {
        return _header.TextSegment.MemoryOffset;
    }
}
