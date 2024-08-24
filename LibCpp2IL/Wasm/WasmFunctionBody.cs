using System.Collections.Generic;

namespace LibCpp2IL.Wasm;

public class WasmFunctionBody
{
    public ulong BodySize;
    public ulong LocalCount;
    public readonly List<WasmLocalEntry> Locals = [];
    public long InstructionsOffset;
    public byte[] Instructions;

    public WasmFunctionBody(WasmFile file)
    {
        BodySize = file.BaseStream.ReadLEB128Unsigned();
        var bodyStartOffset = file.Position;
        LocalCount = file.BaseStream.ReadLEB128Unsigned();
        for (var i = 0UL; i < LocalCount; i++)
        {
            Locals.Add(new(file));
        }

        InstructionsOffset = file.Position;
        Instructions = file.ReadByteArrayAtRawAddress(InstructionsOffset, (int)(bodyStartOffset + (long)BodySize - InstructionsOffset));
    }
}
