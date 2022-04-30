using System.Text;

namespace Arm64Disassembler;

public struct Arm64Instruction
{
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

    public long MemOffset { get; internal set; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Mnemonic);
        sb.Append(" ");

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
        switch (kind)
        {
            case Arm64OperandKind.Register:
                if (comma)
                    sb.Append(", ");

                sb.Append(reg);
                break;
            case Arm64OperandKind.Immediate:
                if (comma)
                    sb.Append(", ");

                sb.Append(imm);
                break;
            case Arm64OperandKind.Memory:
                if (comma)
                    sb.Append(", ");

                sb.Append('[').Append(MemBase);

                if (MemOffset != 0)
                {
                    sb.Append(' ')
                        .Append(MemOffset < 0 ? '-' : '+')
                        .Append(' ')
                        .Append(Math.Abs(MemOffset));
                }

                sb.Append(']');
                break;

            case Arm64OperandKind.None:
                return false;
        }

        return true;
    }
}