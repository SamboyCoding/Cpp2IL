namespace WasmDisassembler;

public struct WasmInstruction
{
    public uint Ip;
    public uint NextIp;
    public WasmMnemonic Mnemonic;
    public object[] Operands;

    public override string ToString()
    {
        if (Operands.Length == 0)
            return $"0x{Ip:X} {Mnemonic}";

        return $"0x{Ip:X} {Mnemonic} {string.Join(", ", Operands)}";
    }
}
