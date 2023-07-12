using Cpp2IL.Core.ISIL;
using Iced.Intel;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.InstructionSets.X86;

public static class X86Extensions
{
    public static InstructionSetIndependentOperand MakeIndependent(this Register reg) => InstructionSetIndependentOperand.MakeRegister(reg.ToString().ToLower());

    public static ulong GetImmediateSafe(this Instruction instruction, int op) => instruction.GetOpKind(op).IsImmediate() ? instruction.GetImmediate(op) : 0;

    public static bool IsJump(this Mnemonic mnemonic) => mnemonic is Mnemonic.Call or >= Mnemonic.Ja and <= Mnemonic.Js;
    public static bool IsConditionalJump(this Mnemonic mnemonic) => mnemonic.IsJump() && mnemonic != Mnemonic.Jmp && mnemonic != Mnemonic.Call;
    
    public static bool IsConditionalMove(this Instruction instruction)
    {
        switch (instruction.Mnemonic)
        {
            case Mnemonic.Cmove:
            case Mnemonic.Cmovne:
            case Mnemonic.Cmovs:
            case Mnemonic.Cmovns:
            case Mnemonic.Cmovg:
            case Mnemonic.Cmovge:
            case Mnemonic.Cmovl:
            case Mnemonic.Cmovle:
            case Mnemonic.Cmova:
            case Mnemonic.Cmovae:
            case Mnemonic.Cmovb:
            case Mnemonic.Cmovbe:
                return true;
            default:
                return false;
        }
    }

    public static bool IsImmediate(this OpKind opKind) => opKind is >= OpKind.Immediate8 and <= OpKind.Immediate32to64;
}
