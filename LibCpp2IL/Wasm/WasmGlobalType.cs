namespace LibCpp2IL.Wasm;

public class WasmGlobalType(WasmFile readFrom)
{
    public WasmTypeEnum Type = (WasmTypeEnum)readFrom.ReadByte();
    public byte Mutability = readFrom.ReadByte();
}
