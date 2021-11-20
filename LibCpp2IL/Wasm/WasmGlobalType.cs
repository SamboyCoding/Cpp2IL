namespace LibCpp2IL.Wasm
{
    public class WasmGlobalType
    {
        public WasmTypeEnum Type;
        public byte Mutability;

        public WasmGlobalType(WasmFile readFrom)
        {
            Type = (WasmTypeEnum) readFrom.ReadByte();
            Mutability = readFrom.ReadByte();
        }
    }
}