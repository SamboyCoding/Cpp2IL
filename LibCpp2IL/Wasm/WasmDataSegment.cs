namespace LibCpp2IL.Wasm;

public class WasmDataSegment
{
    public ulong Index;
    public ConstantExpression? OffsetExpr;
    public long FileOffset;
    public ulong Size;
    public byte[] Data;

    public WasmDataSegment(WasmFile readFrom)
    {
        var mode = readFrom.ReadByte();
        if (mode == 2)
            Index = readFrom.BaseStream.ReadLEB128Unsigned();
        else
            Index = 0;

        if (mode is 0 or 2)
            //"Active" segment
            OffsetExpr = new(readFrom);
        else
            OffsetExpr = null;

        Size = readFrom.BaseStream.ReadLEB128Unsigned();
        FileOffset = readFrom.Position;
        Data = readFrom.ReadByteArrayAtRawAddress(FileOffset, (int)Size);
    }

    public ulong VirtualOffset => OffsetExpr?.Type != ConstantExpression.ConstantInstruction.I32_CONST ? ulong.MaxValue : (ulong)OffsetExpr.Value!;
}
