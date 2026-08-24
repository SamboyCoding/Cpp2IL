namespace WasmDisassembler;

public static class Disassembler
{
    public static List<WasmInstruction> Disassemble(byte[] body, uint virtualAddress)
    {
        var ret = new List<WasmInstruction>();

        using var s = new MemoryStream(body);
        using var reader = new BinaryReader(s);
        while (s.Position < s.Length)
        {
            var ip = virtualAddress + (uint)s.Position;
            var mnemonic = (WasmMnemonic)reader.ReadByte();

            if (mnemonic > WasmMnemonic.LastValid)
                throw new($"Encountered invalid mnemonic {mnemonic} at ip 0x{ip:X}, byte array position {s.Position}.");

            var instruction = reader.ReadInstruction(mnemonic);
            instruction.Ip = ip;
            instruction.NextIp = virtualAddress + (uint)s.Position; //Next ip is position we go into the next instruction with
            ret.Add(instruction);
        }

        return ret;
    }

    private static WasmInstruction ReadInstruction(this BinaryReader reader, WasmMnemonic mnemonic)
    {
        var s = reader.BaseStream;
        var ret = new WasmInstruction { Mnemonic = mnemonic };

        if (mnemonic is >= WasmMnemonic.I32Load and <= WasmMnemonic.I64Store32)
        {
            //Align, offset
            ret.Operands = [s.ReadLEB128Unsigned(), s.ReadLEB128Unsigned()];
            return ret;
        }

        switch (mnemonic)
        {
            case WasmMnemonic.Block:
            case WasmMnemonic.Loop:
            case WasmMnemonic.If:
                //block type, signed so the shorthand value types decode as negatives (non-negative is a type index)
                ret.Operands = [s.ReadLEB128Signed()];
                break;
            case WasmMnemonic.LocalGet:
            case WasmMnemonic.LocalSet:
            case WasmMnemonic.LocalTee:
            case WasmMnemonic.GlobalGet:
            case WasmMnemonic.GlobalSet:
            case WasmMnemonic.Br:
            case WasmMnemonic.BrIf:
            case WasmMnemonic.Call:
            case WasmMnemonic.MemorySize:
            case WasmMnemonic.MemoryGrow:
                ret.Operands = [s.ReadLEB128Unsigned()];
                break;
            case WasmMnemonic.BrTable:
            {
                var count = s.ReadLEB128Unsigned();
                var labels = new ulong[count];
                for (var i = 0UL; i < count; i++)
                    labels[i] = s.ReadLEB128Unsigned();

                //Labels, default label
                ret.Operands = [labels, s.ReadLEB128Unsigned()];
                break;
            }
            case WasmMnemonic.CallIndirect:
                //Type, table
                ret.Operands = [s.ReadLEB128Unsigned(), s.ReadLEB128Unsigned()];
                break;
            case WasmMnemonic.I32Const:
            case WasmMnemonic.I64Const:
                ret.Operands = [s.ReadLEB128Signed()];
                break;
            case WasmMnemonic.F32Const:
                ret.Operands = [reader.ReadSingle()];
                break;
            case WasmMnemonic.F64Const:
                ret.Operands = [reader.ReadDouble()];
                break;
            default:
                ret.Operands = [];
                break;
        }

        return ret;
    }
}
