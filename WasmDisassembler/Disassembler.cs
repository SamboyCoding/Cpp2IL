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
            var ip = virtualAddress + (uint) s.Position;
            var mnemonic = (WasmMnemonic) reader.ReadByte();

            if (mnemonic > WasmMnemonic.LastValid)
                throw new($"Encountered invalid mnemonic {mnemonic} at ip 0x{ip:X}, byte array position {s.Position}.");

            var instruction = reader.ReadInstruction(mnemonic);
            instruction.Ip = ip;
            instruction.NextIp = virtualAddress + (uint) s.Position; //Next ip is position we go into the next instruction with
            ret.Add(instruction);
        }

        return ret;
    }

    private static WasmInstruction ReadInstruction(this BinaryReader reader, WasmMnemonic mnemonic)
    {
        var opTypes = mnemonic.GetOperandTypes();
        return new WasmInstruction
        {
            Mnemonic = mnemonic,
            Operands = opTypes.Length == 0 ? Array.Empty<object>() : opTypes.Select(reader.ReadPrimitive).ToArray(),
        };
    }

    private static Type[] GetOperandTypes(this WasmMnemonic mnemonic)
    {
        if (mnemonic is >= WasmMnemonic.I32Load and <= WasmMnemonic.I64Store32)
            //Align, offset
            return new[] {typeof(LEB128), typeof(LEB128)};

        switch (mnemonic)
        {
            case WasmMnemonic.If:
            case WasmMnemonic.Block:
            case WasmMnemonic.Loop:
            case WasmMnemonic.LocalGet:
            case WasmMnemonic.LocalSet:
            case WasmMnemonic.GlobalGet:
            case WasmMnemonic.GlobalSet:
            case WasmMnemonic.LocalTee:
            case WasmMnemonic.BrIf:
            case WasmMnemonic.Br:
                return new[] {typeof(byte)};
            case WasmMnemonic.I32Const:
            case WasmMnemonic.I64Const:
            case WasmMnemonic.Call:
                return new[] {typeof(LEB128)};
            case WasmMnemonic.F32Const:
                return new[] {typeof(float)};
            case WasmMnemonic.F64Const:
                return new[] {typeof(double)};
            case WasmMnemonic.CallIndirect:
                //Type, table
                return new[] {typeof(LEB128), typeof(byte)};
            default:
                return Array.Empty<Type>();
        }
    }

    internal static object ReadPrimitive(this BinaryReader reader, Type type)
    {
        if (type == typeof(bool))
            return reader.ReadBoolean();

        if (type == typeof(char))
            return reader.ReadChar();

        if (type == typeof(int))
            return reader.ReadInt32();

        if (type == typeof(uint))
            return reader.ReadUInt32();

        if (type == typeof(short))
            return reader.ReadInt16();

        if (type == typeof(ushort))
            return reader.ReadUInt16();

        if (type == typeof(sbyte))
            return reader.ReadSByte();

        if (type == typeof(byte))
            return reader.ReadByte();

        if (type == typeof(long))
            return reader.ReadInt64();

        if (type == typeof(ulong))
            return reader.ReadUInt64();

        if (type == typeof(float))
            return reader.ReadSingle();

        if (type == typeof(double))
            return reader.ReadDouble();

        if (type == typeof(LEB128))
            return reader.BaseStream.ReadLEB128Unsigned();

        throw new($"Bad primitive type: {type}");
    }
}