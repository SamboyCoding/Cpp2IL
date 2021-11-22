namespace WasmDisassembler;

public struct WasmInstruction
{
    public uint Ip;
    public WasmMnemonic Mnemonic;
    public object[] Operands = Array.Empty<object>();

    public override string ToString()
    {
        if (Operands.Length == 0)
            return $"0x{Ip:X} {Mnemonic}"; 
        
        return $"0x{Ip:X} {Mnemonic} {string.Join(", ", Operands)}";
    }
}