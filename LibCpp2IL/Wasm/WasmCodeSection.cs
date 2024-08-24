using System.Collections.Generic;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Wasm;

public class WasmCodeSection : WasmSection
{
    private readonly WasmFile _file;
    public ulong FunctionCount;
    public readonly List<WasmFunctionBody> Functions = [];

    internal WasmCodeSection(WasmSectionId type, long pointer, ulong size, WasmFile file) : base(type, pointer, size)
    {
        _file = file;
        FunctionCount = file.BaseStream.ReadLEB128Unsigned();
        for (var i = 0UL; i < FunctionCount; i++)
        {
            Functions.Add(new(file));
        }

        LibLogger.VerboseNewline($"\t\tRead {Functions.Count} function bodies");
    }

    public byte[] RawSectionContent => _file.GetRawBinaryContent().SubArray((int)Pointer, (int)Size);
}
