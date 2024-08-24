namespace LibCpp2IL.Wasm;

public class WasmLocalEntry(WasmFile file)
{
    public ulong Count = file.BaseStream.ReadLEB128Unsigned();
    public byte Type = file.ReadByte();
}
