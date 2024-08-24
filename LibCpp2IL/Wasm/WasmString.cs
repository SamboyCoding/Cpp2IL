using System.Text;

namespace LibCpp2IL.Wasm;

public class WasmString
{
    public ulong Size;
    public string Value;

    public WasmString(WasmFile readFrom)
    {
        Size = readFrom.BaseStream.ReadLEB128Unsigned();
        Value = Encoding.UTF8.GetString(readFrom.ReadByteArrayAtRawAddress(readFrom.Position, (int)Size));
    }

    public static implicit operator string(WasmString @this) => @this.Value;

    public override string ToString() => this;
}
