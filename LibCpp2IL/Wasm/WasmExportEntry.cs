namespace LibCpp2IL.Wasm;

public class WasmExportEntry(WasmFile file)
{
    public WasmString Name = new(file);
    public WasmExternalKind Kind = (WasmExternalKind)file.ReadByte();
    public ulong Index = file.BaseStream.ReadLEB128Unsigned();
}
