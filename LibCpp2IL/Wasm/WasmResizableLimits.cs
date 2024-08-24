namespace LibCpp2IL.Wasm;

public class WasmResizableLimits
{
    public byte Flags;
    public ulong Initial;
    public ulong Max;

    public WasmResizableLimits(WasmFile readFrom)
    {
        Flags = readFrom.ReadByte();
        Initial = readFrom.BaseStream.ReadLEB128Unsigned();

        if (Flags == 1)
            Max = readFrom.BaseStream.ReadLEB128Unsigned();
    }
}
