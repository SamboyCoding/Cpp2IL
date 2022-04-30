namespace Arm64Disassembler;

public struct Arm64Instruction
{
    public Arm64Mnemonic Mnemonic;

    public Arm64OperandKind Op0Kind;
    public Arm64OperandKind Op1Kind;
    public Arm64OperandKind Op2Kind;
    
    public Arm64Register Op0Reg;
    public Arm64Register Op1Reg;
    public Arm64Register Op2Reg;
    
    public uint Op0Imm;
    public uint Op1Imm;
    public uint Op2Imm;

    public Arm64Register MemBase;
    public long MemOffset;
}