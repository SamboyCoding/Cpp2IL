using System.Collections.Generic;

namespace LibCpp2IL.Wasm;

public class WasmFunctionSection : WasmSection
{
    public ulong EntryCount;
    public readonly List<ulong> Types = [];

    internal WasmFunctionSection(WasmSectionId type, long pointer, ulong size, WasmFile file) : base(type, pointer, size)
    {
        EntryCount = file.BaseStream.ReadLEB128Unsigned();
        for (var i = 0UL; i < EntryCount; i++)
        {
            Types.Add(file.BaseStream.ReadLEB128Unsigned());
        }
    }
}
