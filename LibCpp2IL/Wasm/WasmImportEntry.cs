using System;

namespace LibCpp2IL.Wasm;

public class WasmImportEntry
{
    public WasmString Module;
    public WasmString Field;
    public WasmExternalKind Kind;

    public ulong FunctionEntry;
    public WasmTableType? TableEntry;
    public WasmResizableLimits? MemoryEntry;
    public WasmGlobalType? GlobalEntry;

    public long StartOffset;
    public long EndOffset;

    public WasmImportEntry(WasmFile readFrom)
    {
        Module = new(readFrom);
        Field = new(readFrom);
        Kind = (WasmExternalKind)readFrom.ReadByte();

        switch (Kind)
        {
            case WasmExternalKind.EXT_FUNCTION:
                FunctionEntry = readFrom.BaseStream.ReadLEB128Unsigned();
                break;
            case WasmExternalKind.EXT_TABLE:
                TableEntry = new(readFrom);
                break;
            case WasmExternalKind.EXT_MEMORY:
                MemoryEntry = new(readFrom);
                break;
            case WasmExternalKind.EXT_GLOBAL:
                GlobalEntry = new(readFrom);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public override string ToString() => $"{Module}.{Field} (Type {Kind})";
}
