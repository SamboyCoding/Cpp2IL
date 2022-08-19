using System.Text;
using Arm64Disassembler.InternalDisassembly;

namespace Arm64Disassembler;

public struct Arm64Instruction
{
    public ulong Address { get; internal set; }
    public Arm64Mnemonic Mnemonic { get; internal set; }

    public Arm64OperandKind Op0Kind { get; internal set; }
    public Arm64OperandKind Op1Kind { get; internal set; }
    public Arm64OperandKind Op2Kind { get; internal set; }

    public Arm64Register Op0Reg { get; internal set; }
    public Arm64Register Op1Reg { get; internal set; }
    public Arm64Register Op2Reg { get; internal set; }
    public uint Op0Imm { get; internal set; }
    public uint Op1Imm { get; internal set; }
    public uint Op2Imm { get; internal set; }

    public Arm64Register MemBase { get; internal set; }
    public bool MemIsPreIndexed { get; internal set; }

    public long MemOffset { get; internal set; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append("0x");
        sb.Append(Address.ToString("X8"));
        sb.Append(' ');
        sb.Append(Mnemonic);
        sb.Append(' ');

        if (!AppendOperand(sb, Op0Kind, Op0Reg, Op0Imm))
            return sb.ToString();
        if (!AppendOperand(sb, Op1Kind, Op1Reg, Op1Imm, true))
            return sb.ToString();
        if (!AppendOperand(sb, Op2Kind, Op2Reg, Op2Imm, true))
            return sb.ToString();

        return sb.ToString();
    }

    private bool AppendOperand(StringBuilder sb, Arm64OperandKind kind, Arm64Register reg, uint imm, bool comma = false)
    {
        if (kind == Arm64OperandKind.None)
            return false;

        if (comma)
            sb.Append(", ");

        if (kind == Arm64OperandKind.Register)
            sb.Append(reg);
        else if (kind == Arm64OperandKind.Immediate)
            sb.Append(imm);
        else if (kind == Arm64OperandKind.Memory) 
            AppendMemory(sb);

        return true;
    }

    private void AppendMemory(StringBuilder sb)
    {
        sb.Append('[').Append(MemBase.ToString());

        if (MemOffset != 0)
        {
            sb.Append(' ')
                .Append(MemOffset < 0 ? '-' : '+')
                .Append(" 0x")
                .Append(Math.Abs(MemOffset).ToString("X"));
        }

        sb.Append(']');

        if (MemIsPreIndexed)
            sb.Append('!');
    }
}