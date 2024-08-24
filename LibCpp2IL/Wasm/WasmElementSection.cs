using System.Collections.Generic;

namespace LibCpp2IL.Wasm;

public class WasmElementSection : WasmSection
{
    public ulong ElementCount;
    public readonly List<WasmElementSegment> Elements = [];

    internal WasmElementSection(WasmSectionId type, long pointer, ulong size, WasmFile file) : base(type, pointer, size)
    {
        ElementCount = file.BaseStream.ReadLEB128Unsigned();
        for (var i = 0UL; i < ElementCount; i++)
        {
            Elements.Add(new(file));
        }
    }
}
