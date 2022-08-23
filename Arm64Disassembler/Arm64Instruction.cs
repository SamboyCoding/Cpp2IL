using System.Text;
using Arm64Disassembler.InternalDisassembly;

namespace Arm64Disassembler;

public struct Arm64Instruction
{
    public Arm64Instruction()
    {
        //Default initializer
        Address = 0;
        Mnemonic = Arm64Mnemonic.INVALID;
        Op0Kind = Arm64OperandKind.None;
        Op1Kind = Arm64OperandKind.None;
        Op2Kind = Arm64OperandKind.None;
        Op3Kind = Arm64OperandKind.None;
        Op0Reg = Arm64Register.INVALID;
        Op1Reg = Arm64Register.INVALID;
        Op2Reg = Arm64Register.INVALID;
        Op3Reg = Arm64Register.INVALID;
        Op0Imm = 0;
        Op1Imm = 0;
        Op2Imm = 0;
        Op3Imm = 0;
        MemBase = Arm64Register.INVALID;
        MemIsPreIndexed = false;
        MemOffset = 0;
        
        //These lines are the ONLY reason this constructor needs to exist because they define 0 as a valid value.
        ConditionCode = Arm64ConditionCode.NONE; 
        ExtendType = Arm64ExtendType.NONE;
        ShiftType = Arm64ShiftType.NONE;
    }

    public ulong Address { get; internal set; }
    public Arm64Mnemonic Mnemonic { get; internal set; }
    public Arm64ConditionCode ConditionCode { get; internal set; }

    public Arm64OperandKind Op0Kind { get; internal set; }
    public Arm64OperandKind Op1Kind { get; internal set; }
    public Arm64OperandKind Op2Kind { get; internal set; }
    public Arm64OperandKind Op3Kind { get; internal set; }

    public Arm64Register Op0Reg { get; internal set; }
    public Arm64Register Op1Reg { get; internal set; }
    public Arm64Register Op2Reg { get; internal set; }
    public Arm64Register Op3Reg { get; internal set; }
    public long Op0Imm { get; internal set; }
    public long Op1Imm { get; internal set; }
    public long Op2Imm { get; internal set; }
    public long Op3Imm { get; internal set; }

    public Arm64Register MemBase { get; internal set; }
    public bool MemIsPreIndexed { get; internal set; }
    public long MemOffset { get; internal set; }
    
    public Arm64ExtendType ExtendType { get; internal set; }
    public Arm64ShiftType ShiftType { get; internal set; }
    
    public ulong BranchTarget => Mnemonic is Arm64Mnemonic.B or Arm64Mnemonic.BL 
        ? (ulong) ((long) Address + Op0Imm) //Casting is a bit weird here because we want to return an unsigned long (can't jump to negative), but the immediate needs to be signed.
        : throw new("Branch target not available for this instruction, must be a B or BL");

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append("0x");
        sb.Append(Address.ToString("X8"));
        sb.Append(' ');
        sb.Append(Mnemonic);

        if (ConditionCode != Arm64ConditionCode.NONE)
            sb.Append('.').Append(ConditionCode);
            
        sb.Append(' ');

        if (!AppendOperand(sb, Op0Kind, Op0Reg, Op0Imm))
            return sb.ToString();
        if (!AppendOperand(sb, Op1Kind, Op1Reg, Op1Imm, true))
            return sb.ToString();
        if (!AppendOperand(sb, Op2Kind, Op2Reg, Op2Imm, true))
            return sb.ToString();
        
        if (ExtendType != Arm64ExtendType.NONE)
            sb.Append(", ").Append(ExtendType);

        if (ShiftType != Arm64ShiftType.NONE)
            sb.Append(", ").Append(ShiftType);
        
        if (!AppendOperand(sb, Op3Kind, Op3Reg, Op3Imm, true))
            return sb.ToString();

        return sb.ToString();
    }

    private bool AppendOperand(StringBuilder sb, Arm64OperandKind kind, Arm64Register reg, long imm, bool comma = false)
    {
        if (kind == Arm64OperandKind.None)
            return false;

        if (comma)
            sb.Append(", ");

        if (kind == Arm64OperandKind.Register)
            sb.Append(reg);
        else if (kind == Arm64OperandKind.Immediate)
            sb.Append("0x").Append(imm.ToString("X"));
        else if(kind == Arm64OperandKind.ImmediatePcRelative)
            sb.Append("0x").Append(((long) Address + imm).ToString("X"));
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