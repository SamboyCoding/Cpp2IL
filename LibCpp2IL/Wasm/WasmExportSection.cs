using System.Collections.Generic;

namespace LibCpp2IL.Wasm
{
    public class WasmExportSection : WasmSection
    {
        public ulong ExportCount;
        public readonly List<WasmExportEntry> Exports = new();

        internal WasmExportSection(WasmSectionId type, long pointer, ulong size, WasmFile file) : base(type, pointer, size)
        {
            ExportCount = file.BaseStream.ReadLEB128Unsigned();
            for (var i = 0UL; i < ExportCount; i++)
            {
                Exports.Add(new(file));
            }
        }
    }
}