namespace LibCpp2IL.Wasm
{
    public class WasmLocalEntry
    {
        public ulong Count;
        public byte Type;

        public WasmLocalEntry(WasmFile file)
        {
            Count = file.BaseStream.ReadLEB128Unsigned();
            Type = file.ReadByte();
        }
    }
}