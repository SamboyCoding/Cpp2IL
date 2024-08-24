using System.Collections.Generic;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Wasm;

public class WasmExportSection : WasmSection
{
    public ulong ExportCount;
    public readonly List<WasmExportEntry> Exports = [];

    internal WasmExportSection(WasmSectionId type, long pointer, ulong size, WasmFile file) : base(type, pointer, size)
    {
        ExportCount = file.BaseStream.ReadLEB128Unsigned();
        for (var i = 0UL; i < ExportCount; i++)
        {
            var export = new WasmExportEntry(file);
            // if(export.Kind == WasmExternalKind.EXT_FUNCTION)
            // LibLogger.VerboseNewline($"\t\t\t- Found exported function {export.Name}");
            Exports.Add(export);
        }

        LibLogger.VerboseNewline($"\t\tRead {Exports.Count} exported functions");
    }
}
