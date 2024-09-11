using System;
using System.Globalization;
using System.Text;
using Disarm;
using Disarm.InternalDisassembly;

namespace Cpp2IL.Core.InstructionSets;

public  static class Arm64InsExtensions
{
    private static string FixReg(this string reg)
    {
        if (reg.StartsWith("v"))
        {
            return reg.Replace("v", "s");
        }

        return reg;
    }
    private static void AppendMemory(Arm64Instruction instruction,StringBuilder sb)
    {
        sb.Append('[').Append(instruction.MemBase.ToString().ToLowerInvariant());
        
        if(instruction.MemAddendReg != Arm64Register.INVALID)
            sb.Append(", ").Append(instruction.MemAddendReg.ToString().ToLowerInvariant());

        if (instruction.MemOffset != 0)
        {
            if (instruction.MemOffset  < 0 )
            {
                if (instruction.MemOffset >-0x10)
                {
                    sb
                        .Append("#")
                        .Append(Math.Abs(instruction.MemOffset).ToString("X").ToLowerInvariant()); 
                }
                else
                {
                    sb
                        .Append("#0x")
                        .Append(Math.Abs(instruction.MemOffset).ToString("X").ToLowerInvariant()); 
                }
            
            }
            else
            {
                if (instruction.MemOffset>=0x10)
                {
                    sb.Append(instruction.MemOffset < 0 ? ", #-" : ", #")
                        .Append("0x")
                        .Append(Math.Abs(instruction.MemOffset).ToString("X").ToLowerInvariant());
                }
                else
                {
                    sb.Append(", #").Append(instruction.MemOffset.ToString("X").ToLowerInvariant());
                }
              
            }
           
        }
        
        if(instruction.MemExtendType != Arm64ExtendType.NONE)
            sb.Append(", ").Append(instruction.MemExtendType.ToString().ToLowerInvariant());
        else if(instruction.MemShiftType != Arm64ShiftType.NONE)
            sb.Append(", ").Append(instruction.MemShiftType.ToString().ToLowerInvariant());
        
        if(instruction.MemExtendOrShiftAmount != 0)
            sb.Append(" #").Append(instruction.MemExtendOrShiftAmount.ToString().ToLowerInvariant());

        sb.Append(']');

        if (instruction.MemIsPreIndexed)
            sb.Append('!');
    }

    private static bool AppendOperand(Arm64Instruction instruction,StringBuilder sb, Arm64OperandKind kind, 
        Arm64Register reg, Arm64VectorElement vectorElement, Arm64ArrangementSpecifier regArrangement, Arm64ShiftType shiftType, long imm, double fpImm, bool comma = false)
    {
        if (kind == Arm64OperandKind.None)
            return false;

        if (comma)
            sb.Append(", ");

        if (kind == Arm64OperandKind.Register)
        {
            sb.Append(reg.ToString().ToLowerInvariant().FixReg());

            if (regArrangement != Arm64ArrangementSpecifier.None)
                sb.Append('.').Append(regArrangement.ToDisassemblyString());
        } else if (kind == Arm64OperandKind.VectorRegisterElement)
        {
            sb.Append(reg)
                .Append('.')
                .Append(vectorElement);
        }
        else if (kind == Arm64OperandKind.Immediate)
        {
            if (shiftType != Arm64ShiftType.NONE)
                sb.Append(shiftType).Append(' ');
            sb.Append("0x").Append(imm.ToString("X").ToLowerInvariant());
        } else if (kind == Arm64OperandKind.FloatingPointImmediate)
        {
            sb.Append(fpImm.ToString(CultureInfo.InvariantCulture));
        }
        else if(kind == Arm64OperandKind.ImmediatePcRelative)
            sb.Append("0x").Append(((long) instruction.Address + imm).ToString("X").ToLowerInvariant());
        else if (kind == Arm64OperandKind.Memory) 
            AppendMemory(instruction,sb);

        return true;
    }

    public static string FixString(Arm64Instruction instruction)
    {   
        var sb = new StringBuilder();

      
        sb.Append(instruction.Mnemonic.ToString().ToLowerInvariant());

        if (instruction.MnemonicConditionCode != Arm64ConditionCode.NONE)
            sb.Append('.').Append(instruction.MnemonicConditionCode.ToString().ToLowerInvariant());
            
        sb.Append(' ');

        //Ew yes I'm using goto.
        if (!AppendOperand(instruction,sb, instruction.Op0Kind, instruction.Op0Reg, instruction.Op0VectorElement,
                instruction.Op0Arrangement, instruction.Op1ShiftType, instruction.Op0Imm, instruction.Op0FpImm))
            goto doneops;
        if (!AppendOperand(instruction,sb, instruction.Op1Kind, instruction.Op1Reg,instruction. Op1VectorElement,instruction. Op1Arrangement, 
                instruction.Op1ShiftType, instruction.Op1Imm, instruction.Op1FpImm, true))
            goto doneops;
        if (!AppendOperand(instruction,sb, instruction.Op2Kind, instruction.Op2Reg,instruction. Op2VectorElement,instruction. Op2Arrangement, 
                instruction.Op1ShiftType, instruction.Op2Imm, instruction.Op2FpImm, true))
            goto doneops;
        if (!AppendOperand(instruction,sb, instruction.Op3Kind, instruction.Op3Reg, instruction.Op3VectorElement,instruction. Op3Arrangement, 
                instruction.Op1ShiftType, instruction.Op3Imm, instruction.Op3FpImm, true))
            goto doneops;
        
        doneops:
        if (instruction.FinalOpExtendType != Arm64ExtendType.NONE)
            sb.Append(", ").Append(instruction.FinalOpExtendType);
        else if (instruction.FinalOpShiftType != Arm64ShiftType.NONE)
            sb.Append(", ").Append(instruction.FinalOpShiftType);
        else if (instruction.FinalOpConditionCode != Arm64ConditionCode.NONE)
            sb.Append(", ").Append(instruction.FinalOpConditionCode);

        return sb.ToString();
    }
}
