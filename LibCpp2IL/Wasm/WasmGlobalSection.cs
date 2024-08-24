using System.Collections.Generic;

namespace LibCpp2IL.Wasm;

public class WasmGlobalSection : WasmSection
{
    public ulong GlobalCount;
    public readonly List<WasmGlobalEntry> Globals = [];

    internal WasmGlobalSection(WasmSectionId type, long pointer, ulong size, WasmFile file) : base(type, pointer, size)
    {
        GlobalCount = file.BaseStream.ReadLEB128Unsigned();
        for (var i = 0UL; i < GlobalCount; i++)
        {
            Globals.Add(new(file));
        }
    }
}
