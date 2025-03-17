using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Logging;

namespace LibCpp2IL.MachO;

public class MachOUniversalFile : ClassReadingBinaryReader
{
    public MachOFile BestMachOFile { get; }

    public override float MetadataVersion => BestMachOFile?.MetadataVersion ?? 0;

    private static readonly MachOCpuType[] OrderedSupportedCpuTypes =
    [
        MachOCpuType.CPU_TYPE_X86_64,
        MachOCpuType.CPU_TYPE_ARM64,
        MachOCpuType.CPU_TYPE_I386,
        MachOCpuType.CPU_TYPE_ARM,
        MachOCpuType.CPU_TYPE_ARM64_32
    ];

    public MachOUniversalFile(MemoryStream input) : base(input)
    {
        LibLogger.VerboseNewline("Reading universal Mach-O file...");

        LibLogger.VerboseNewline("\tReading universal Mach-O header...");
        var header = ReadReadable<MachOUniversalHeader>();
        if (header.NumberFileEntries == 0)
            throw new("Cannot read a Mach-O file from the universal Mach-O container: no file entries exists.");

        LibLogger.VerboseNewline($"\tFound {header.NumberFileEntries} Mach-O file entries.");
        var entries = new List<MachOUniversalFileEntry>();
        LibLogger.VerboseNewline("\tReading Mach-O file entries...");
        for (var i = 0; i < header.NumberFileEntries; i++)
            entries.Add(ReadReadable<MachOUniversalFileEntry>());

        LibLogger.VerboseNewline("\tSelecting a suitable Mach-O file entry to read...");
        var selectedEntry = entries
            .Where(x => OrderedSupportedCpuTypes.Contains(x.CpuType))
            .Select(x => (entry: x, supportOrder: Array.IndexOf(OrderedSupportedCpuTypes, x.CpuType)))
            .OrderBy(x => x.supportOrder)
            .Select(x => x.entry)
            .FirstOrDefault();

        if (selectedEntry is null)
        {
            selectedEntry = entries[0];
            LibLogger.WarnNewline($"\tCouldn't find a Mach-O file entry with a supported CPU architecture, " +
                                  $"falling back to the first entry with CPU type {selectedEntry.CpuType}");
        }
        else
        {
            LibLogger.VerboseNewline($"\tSelected Mach-O file entry with CPU type: {selectedEntry.CpuType}");
        }

        LibLogger.VerboseNewline($"\tReading Mach-O file at offset 0x{selectedEntry.FileOffset:X} for 0x{selectedEntry.FileSize:X} bytes");
        input.Seek((int)selectedEntry.FileOffset, SeekOrigin.Begin);
        var machOFileBuffer = new byte[selectedEntry.FileSize];
        var numberBytesRead = input.Read(machOFileBuffer, 0, (int)selectedEntry.FileSize);
        if (numberBytesRead != (int)selectedEntry.FileSize)
            LibLogger.WarnNewline($"Read {numberBytesRead} bytes, but expected to read {selectedEntry.FileSize} bytes");
        var memoryStream = new MemoryStream(machOFileBuffer, 0, (int)selectedEntry.FileSize, false, true);
        BestMachOFile = new MachOFile(memoryStream);
    }
}
