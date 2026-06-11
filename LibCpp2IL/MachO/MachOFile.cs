using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using LibCpp2IL.Logging;
using System.Diagnostics.CodeAnalysis;

namespace LibCpp2IL.MachO;

public class MachOFile : Il2CppBinary
{
    private byte[] _raw;

    private readonly MachOHeader _header;
    private readonly MachOLoadCommand[] _loadCommands;

    private readonly MachOSegmentCommand[] Segments64;
    private readonly MachOSection[] Sections64;
    private readonly Dictionary<string, ulong> _exportAddressesDict;
    private readonly Dictionary<ulong, string> _exportNamesDict;

    public MachOFile(MemoryStream input) : base(input)
    {
        LibLogger.VerboseNewline("Reading Mach-O file...");
        _raw = input.GetBuffer();

        LibLogger.Verbose("\tReading Mach-O header...");
        _header = ReadReadable<MachOHeader>();

        switch (_header.Magic)
        {
            case MachOHeader.MAGIC_32_BIT:
                LibLogger.Verbose("Mach-O is 32-bit...");
                is32Bit = true;
                break;
            case MachOHeader.MAGIC_64_BIT:
                LibLogger.Verbose("Mach-O is 64-bit...");
                is32Bit = false;
                break;
            default:
                throw new($"Unknown Mach-O Magic: {_header.Magic}");
        }

        switch (_header.CpuType)
        {
            case MachOCpuType.CPU_TYPE_I386:
                LibLogger.VerboseNewline("Mach-O contains x86_32 instructions.");
                InstructionSetId = DefaultInstructionSets.X86_32;
                break;
            case MachOCpuType.CPU_TYPE_X86_64:
                LibLogger.VerboseNewline("Mach-O contains x86_64 instructions.");
                InstructionSetId = DefaultInstructionSets.X86_64;
                break;
            case MachOCpuType.CPU_TYPE_ARM:
                LibLogger.VerboseNewline("Mach-O contains ARM (32-bit) instructions.");
                InstructionSetId = DefaultInstructionSets.ARM_V7;
                break;
            case MachOCpuType.CPU_TYPE_ARM64:
                LibLogger.VerboseNewline("Mach-O contains ARM64 instructions.");
                InstructionSetId = DefaultInstructionSets.ARM_V8;
                break;
            default:
                throw new($"Don't know how to handle a Mach-O CPU Type of {_header.CpuType}");
        }

        if (_header.Magic == MachOHeader.MAGIC_32_BIT)
            LibLogger.ErrorNewline("32-bit MACH-O files have not been tested! Please report any issues.");
        else
            LibLogger.WarnNewline("Mach-O Support is experimental. Please open an issue if anything seems incorrect.");

        LibLogger.Verbose("\tReading Mach-O load commands...");
        _loadCommands = ReadReadableArrayAtRawAddr<MachOLoadCommand>(-1, _header.NumLoadCommands);
        LibLogger.VerboseNewline($"Read {_loadCommands.Length} load commands.");

        Segments64 = _loadCommands.Where(c => c.Command == LoadCommandId.LC_SEGMENT_64).Select(c => c.CommandData).Cast<MachOSegmentCommand>().ToArray();
        Sections64 = Segments64.SelectMany(s => s.Sections).ToArray();

        var dyldData = _loadCommands.FirstOrDefault(c => c.Command is LoadCommandId.LC_DYLD_INFO or LoadCommandId.LC_DYLD_INFO_ONLY)?.CommandData as MachODynamicLinkerCommand;
        var exports = dyldData?.Exports ?? [];
        
        _exportAddressesDict = exports.ToDictionary(e => e.Name[1..], e => e.Address); //Skip the first character, which is a leading underscore inserted by the compiler
        _exportNamesDict = new Dictionary<ulong, string>();
        foreach (var export in exports) // there may be duplicate names
        {
            _exportNamesDict[export.Address] = export.Name[1..];
        }

        LibLogger.VerboseNewline($"\tFound {_exportAddressesDict.Count} exports in the DYLD info load command.");
        
        var chainedFixups = _loadCommands.FirstOrDefault(c => c.Command == LoadCommandId.LC_DYLD_CHAINED_FIXUPS)?.CommandData as MachOLinkEditDataCommand;
        if (chainedFixups != null) 
            ApplyChainedFixups(chainedFixups);

        LibLogger.VerboseNewline($"\tMach-O contains {Segments64.Length} segments, split into {Sections64.Length} sections.");
        
        LibLogger.VerboseNewline("Mach-O file read successfully.");
    }

    public override long RawLength => _raw.Length;
    public override byte GetByteAtRawAddress(ulong addr) => _raw[addr];

    public override long MapVirtualAddressToRaw(ulong uiAddr, bool throwOnError = true)
    {
        var sec = Sections64.FirstOrDefault(s => s.Address <= uiAddr && uiAddr < s.Address + s.Size);

        if (sec == null)
            if (throwOnError)
                throw new($"Could not find section for virtual address 0x{uiAddr:X}. Lowest section address is 0x{Sections64.Min(s => s.Address):X}, highest section address is 0x{Sections64.Max(s => s.Address + s.Size):X}");
            else
                return VirtToRawInvalidNoMatch;

        return (long)(sec.Offset + (uiAddr - sec.Address));
    }

    public override ulong MapRawAddressToVirtual(uint offset, bool throwOnError = true)
    {
        var sec = Sections64.FirstOrDefault(s => s.Offset <= offset && offset < s.Offset + s.Size);

        if (sec == null)
            if (throwOnError)
                throw new($"Could not find section for raw address 0x{offset:X}");
            else
                return 0;

        return sec.Address + (offset - sec.Offset);
    }

    public override ulong GetRva(ulong pointer)
    {
        // Mach-O doesn't have RVAs and instead uses virtual addresses, so we can just return the pointer as-is.
        return pointer;
    }

    public override ReadOnlySpan<byte> GetRawBinaryContent() => _raw;

    public override ulong GetVirtualAddressOfExportedFunctionByName(string toFind)
    {
        if (!_exportAddressesDict.TryGetValue(toFind, out var addr))
            return 0;

        return addr;
    }

    public override bool IsExportedFunction(ulong addr) => _exportNamesDict.ContainsKey(addr);

    public override bool TryGetExportedFunctionName(ulong addr, [NotNullWhen(true)] out string? name)
    {
        return _exportNamesDict.TryGetValue(addr, out name);
    }

    public override IEnumerable<KeyValuePair<string, ulong>> GetExportedFunctions()
    {
        return _exportAddressesDict.Select(pair => new KeyValuePair<string, ulong>(pair.Key, pair.Value));
    }

    private MachOSection GetTextSection64()
    {
        var textSection = Sections64.FirstOrDefault(s => s.SectionName == "__text");

        if (textSection == null)
            throw new("Could not find __text section");

        return textSection;
    }

    public override ReadOnlySpan<byte> GetEntirePrimaryExecutableSection()
    {
        var textSection = GetTextSection64();

        return _raw.AsSpan((int)textSection.Offset, (int)textSection.Size);
    }

    public override ulong GetVirtualAddressOfPrimaryExecutableSection() => GetTextSection64().Address;
    
    //Thanks to LukeFZ for this
    private void ApplyChainedFixups(MachOLinkEditDataCommand cmd)
    {
        LibLogger.Verbose("\tApplying chained fixups...");
        var chainedFixupsHeader = ReadReadable<MachODyldChainedFixupsHeader>(cmd.Offset);
        if (chainedFixupsHeader.FixupsVersion != MachODyldChainedFixupsHeader.SupportedFixupsVersion)
        {
            LibLogger.ErrorNewline($"Mach-O: Unsupported fixups version {chainedFixupsHeader.FixupsVersion}, expecting {MachODyldChainedFixupsHeader.SupportedFixupsVersion}");
            return;
        }

        if (chainedFixupsHeader.ImportsFormat != MachODyldChainedFixupsHeader.SupportedImportsFormat)
        {
            LibLogger.ErrorNewline($"Mach-O: Unsupported imports format {chainedFixupsHeader.ImportsFormat}, expecting {MachODyldChainedFixupsHeader.SupportedImportsFormat}");
            return;
        }

        var posBack = Position;
        
        var startsBase = cmd.Offset + chainedFixupsHeader.StartsOffset;
        
        Position = startsBase;
        var segmentCount = ReadUInt32();
        var segmentStartOffsets = ReadClassArrayAtRawAddr<uint>(startsBase + 4, segmentCount);

        Position = posBack;

        var count = 0;
        foreach (var startOffset in segmentStartOffsets)
        {
            if (startOffset == 0)
                continue;
            
            var startsInfo = ReadReadable<MachODyldChainedStartsInSegment>(startsBase + startOffset);
            if (startsInfo.SegmentOffset == 0)
                continue;
            
            var pointerFormat = (MachODyldChainedPtr)startsInfo.PointerFormat;
            var pages = ReadClassArrayAtRawAddr<ushort>(startsBase + startOffset + MachODyldChainedStartsInSegment.Size, startsInfo.PageCount);
            for (var i = 0; i < pages.Length; i++)
            {
                var page = pages[i];
                if (page == MachODyldChainedStartsInSegment.DYLD_CHAINED_PTR_START_NONE)
                    continue;
                var chainOffset = startsInfo.SegmentOffset + (ulong)(i * startsInfo.PageSize) + page;
                while (true)
                {
                    var currentEntry = ReadReadable<MachODyldChainedPtr64Rebase>((long)chainOffset);
                    var fixedValue = 0ul;
                    if (currentEntry.Bind)
                    {
                        //TODO: Bind.
                    }
                    else
                    {
                        fixedValue = pointerFormat switch
                        {
                            MachODyldChainedPtr.DYLD_CHAINED_PTR_64 or MachODyldChainedPtr.DYLD_CHAINED_PTR_64_OFFSET => currentEntry.High8 << 56 | currentEntry.Target,
                            _ => fixedValue
                        };
                        WriteWord((int)chainOffset, fixedValue);
                        count++;
                    }

                    if (currentEntry.Next == 0)
                        break;
                    chainOffset += currentEntry.Next * 4;
                }
            }
        }
        
        LibLogger.VerboseNewline($"Applied {count} chained fixups.");
    }
}
