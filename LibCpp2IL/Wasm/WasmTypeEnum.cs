namespace LibCpp2IL.Wasm;

public enum WasmTypeEnum : byte
{
    i32 = 0x7F,
    i64 = 0x7E,
    f32 = 0x7D,
    f64 = 0x7C,
    funcRef = 0x70,
    externRef = 0x6F,
}
