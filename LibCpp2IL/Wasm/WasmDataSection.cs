using System.Collections.Generic;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Wasm;

public class WasmDataSection : WasmSection
{
    public ulong DataCount;
    public List<WasmDataSegment> DataEntries = [];

    internal WasmDataSection(WasmSectionId type, long pointer, ulong size, WasmFile file) : base(type, pointer, size)
    {
        DataCount = file.BaseStream.ReadLEB128Unsigned();
        for (var i = 0UL; i < DataCount; i++)
        {
            DataEntries.Add(new(file));
        }

        LibLogger.VerboseNewline($"\t\tRead {DataEntries.Count} data segments");
    }
}
