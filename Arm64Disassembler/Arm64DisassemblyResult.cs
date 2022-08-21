namespace Arm64Disassembler;

public readonly struct Arm64DisassemblyResult
{
    public readonly List<Arm64Instruction> Instructions;
    public readonly ulong VirtualAddress;
    
    public ulong EndVirtualAddress => VirtualAddress + (ulong)(Instructions.Count * 4);

    public Arm64DisassemblyResult(List<Arm64Instruction> instructions, ulong virtualAddress)
    {
        Instructions = instructions;
        VirtualAddress = virtualAddress;
    }

    public Arm64DisassemblyResult()
    {
        Instructions = new();
        VirtualAddress = 0;
    }
}