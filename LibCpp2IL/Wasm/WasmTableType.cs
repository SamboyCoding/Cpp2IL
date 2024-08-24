namespace LibCpp2IL.Wasm;

public class WasmTableType(WasmFile readFrom)
{
    public WasmTypeEnum ElemType = (WasmTypeEnum)readFrom.ReadByte();
    public WasmResizableLimits Limits = new(readFrom);
}
