namespace LibCpp2IL.Wasm;

public class WasmGlobalEntry(WasmFile file)
{
    public WasmGlobalType Type = new(file);
    public ConstantExpression Expression = new(file);
}
