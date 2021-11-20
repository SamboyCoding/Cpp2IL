namespace LibCpp2IL.Wasm
{
    public class WasmTableType
    {
        public WasmTypeEnum ElemType;
        public WasmResizableLimits Limits;

        public WasmTableType(WasmFile readFrom)
        {
            ElemType = (WasmTypeEnum) readFrom.ReadByte();
            Limits = new(readFrom);
        }
    }
}