using System.Collections.Generic;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Wasm;

public class WasmTypeSection : WasmSection
{
    public ulong TypeCount;
    public readonly List<WasmTypeEntry> Types = [];

    internal WasmTypeSection(WasmSectionId type, long pointer, ulong size, WasmFile readFrom) : base(type, pointer, size)
    {
        TypeCount = readFrom.BaseStream.ReadLEB128Unsigned();
        for (var i = 0UL; i < TypeCount; i++)
        {
            Types.Add(new WasmTypeEntry(readFrom));
        }

        LibLogger.VerboseNewline($"\t\tRead {Types.Count} function types");
    }
}
