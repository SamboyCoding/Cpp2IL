using System.Collections.Generic;

namespace LibCpp2IL.Wasm;

public class WasmTableSection : WasmSection
{
    public ulong TableCount;
    public readonly List<WasmTableType> Tables = [];

    internal WasmTableSection(WasmSectionId type, long pointer, ulong size, WasmFile file) : base(type, pointer, size)
    {
        TableCount = file.BaseStream.ReadLEB128Unsigned();
        for (var i = 0UL; i < TableCount; i++)
        {
            Tables.Add(new(file));
        }
    }
}
