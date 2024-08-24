using System;

namespace LibCpp2IL.Wasm;

public class ConstantExpression
{
    public ConstantInstruction Type;
    public IConvertible? Value;

    public ConstantExpression(WasmFile file)
    {
        Type = (ConstantInstruction)file.ReadByte();

        switch (Type)
        {
            case ConstantInstruction.GLOBAL_GET:
            case ConstantInstruction.I32_CONST:
            case ConstantInstruction.I64_CONST:
            case ConstantInstruction.REF_FUNC:
                Value = file.BaseStream.ReadLEB128Unsigned();
                break;
            case ConstantInstruction.F32_CONST:
                Value = file.ReadSingle();
                break;
            case ConstantInstruction.F64_CONST:
                Value = file.ReadDouble();
                break;
            case ConstantInstruction.REF_NULL_FUNCREF:
                var subType = file.ReadByte();

                if (subType == 0x6F)
                    Type = ConstantInstruction.REF_NULL_EXTERNREF;
                else if (subType == 0x70)
                    Type = ConstantInstruction.REF_NULL_FUNCREF;
                else
                    throw new($"Invalid subtype {subType}");

                Value = null;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        var end = file.ReadByte();
        if (end != 0x0B)
            throw new($"Invalid end byte, got 0x{end:X2}, expecting 0x0B");
    }

    public enum ConstantInstruction : byte
    {
        I32_CONST = 0x41, /* i32.const n: value is LEB128 */
        I64_CONST = 0x42, /* i64.const n: value is LEB128 */
        F32_CONST = 0x43, /* f32.const z: value is byte[4] */
        F64_CONST = 0x44, /* f64.const z: value is byte[8] */
        REF_NULL_FUNCREF = 0xD0, /* ref.null funcref: value is null */
        REF_NULL_EXTERNREF = 0xD1, /* ref.null externref: value is null */
        REF_FUNC = 0xD2, /* ref.func x: value is LEB128 funcidx */
        GLOBAL_GET = 0x23, /* global.get x: value is LEB128 globalidx */
    }
}
