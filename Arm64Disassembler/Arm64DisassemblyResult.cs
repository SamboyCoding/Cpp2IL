namespace Arm64Disassembler;

public ref struct Arm64DisassemblyResult
{
    public List<Arm64Instruction> Instructions;
    public ulong VirtualAddress;
    public Span<byte> RawBytes;
    public int RawLength => RawBytes.Length;

    public Arm64DisassemblyResult(List<Arm64Instruction> instructions, ulong virtualAddress, Span<byte> rawBytes)
    {
        Instructions = instructions;
        VirtualAddress = virtualAddress;
        RawBytes = rawBytes;
    }

    public Arm64DisassemblyResult()
    {
        Instructions = new();
        RawBytes = Span<byte>.Empty;
        VirtualAddress = 0;
    }
}