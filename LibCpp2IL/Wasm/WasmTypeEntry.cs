using System.Linq;

namespace LibCpp2IL.Wasm;

public class WasmTypeEntry
{
    public int Form;
    public ulong ParamCount;
    public WasmTypeEnum[] ParamTypes;
    public ulong ReturnCount;
    public WasmTypeEnum[] ReturnTypes;

    public WasmTypeEntry(WasmFile file)
    {
        Form = file.ReadByte();
        ParamCount = file.BaseStream.ReadLEB128Unsigned();
        ParamTypes = file.ReadByteArrayAtRawAddress(file.Position, (int)ParamCount).Select(b => (WasmTypeEnum)b).ToArray();
        ReturnCount = file.BaseStream.ReadLEB128Unsigned();
        ReturnTypes = file.ReadByteArrayAtRawAddress(file.Position, (int)ReturnCount).Select(b => (WasmTypeEnum)b).ToArray();
    }
}
