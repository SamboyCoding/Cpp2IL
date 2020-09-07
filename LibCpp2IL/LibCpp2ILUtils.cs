using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace LibCpp2IL
{
    public static class LibCpp2ILUtils
    {
        public static List<Instruction> DisassembleBytes(bool is32Bit, byte[] bytes)
        {
            return new List<Instruction>(new Disassembler(bytes, is32Bit ? ArchitectureMode.x86_32 : ArchitectureMode.x86_64, 0, true).Disassemble());
        }
        
        public static List<Instruction> GetMethodBodyAtRawAddress(PE.PE theDll, long addr, bool peek)
        {
            var ret = new List<Instruction>();
            var con = true;
            var buff = new List<byte>();
            while (con)
            {
                buff.Add(theDll.raw[addr]);

                ret = DisassembleBytes(theDll.is32Bit, buff.ToArray());

                if (ret.All(i => !i.Error) && ret.Any(i => i.Mnemonic == ud_mnemonic_code.UD_Iint3))
                    con = false;

                if (peek && buff.Count > 50)
                    con = false;
                else if (buff.Count > 1000)
                    con = false; //Sanity breakout.

                addr++;
            }

            return ret /*.Where(i => !i.Error).ToList()*/;
        }

        public static ulong GetJumpTarget(Instruction insn, ulong start)
        {
            var opr = insn.Operands[0];

            var mode = GetOprMode(insn);
            
            var num = UInt64.MaxValue >> 64 - mode;
            return opr.Size switch
            {
                8 => (start + (ulong) opr.LvalSByte & num),
                16 => (start + (ulong) opr.LvalSWord & num),
                32 => (start + (ulong) opr.LvalSDWord & num),
                64 => (start + (ulong) opr.LvalSQWord & num),
                _ => throw new InvalidOperationException($"invalid relative offset size {opr.Size}.")
            };
        }
        
        private static FieldInfo oprMode = typeof(Instruction).GetField("opr_mode", BindingFlags.Instance | BindingFlags.NonPublic);

        private static byte GetOprMode(Instruction instruction)
        {
            return (byte) oprMode.GetValue(instruction);
        }
        
        public static ulong GetImmediateValue(Instruction insn, Operand op)
        {
            ulong num;
            if (op.Opcode == ud_operand_code.OP_sI && op.Size != GetOprMode(insn))
            {
                if (op.Size == 8)
                {
                    num = (ulong) op.LvalSByte;
                }
                else
                {
                    if (op.Size != 32)
                        throw new InvalidOperationException("Operand size must be 32");
                    num = (ulong) op.LvalSDWord;
                }

                if (GetOprMode(insn) < 64)
                    num &= (ulong) ((1L << GetOprMode(insn)) - 1L);
            }
            else
            {
                switch (op.Size)
                {
                    case 8:
                        num = op.LvalByte;
                        break;
                    case 16:
                        num = op.LvalUWord;
                        break;
                    case 32:
                        num = op.LvalUDWord;
                        break;
                    case 64:
                        num = op.LvalUQWord;
                        break;
                    default:
                        throw new InvalidOperationException($"Invalid size for operand: {op.Size}");
                }
            }

            return num;
        }
        
        public static ulong GetOffsetFromMemoryAccess(Instruction insn, Operand op)
        {
            var num1 = (ulong) GetOperandMemoryOffset(op);

            if (num1 == 0) return 0;

            return num1 + insn.PC;
        }
        
        public static int GetOperandMemoryOffset(Operand op)
        {
            if (op.Type != ud_type.UD_OP_MEM) return 0;
            var num1 = op.Offset switch
            {
                8 => op.LvalSByte,
                16 => op.LvalSWord,
                32 => op.LvalSDWord,
                _ => 0
            };
            return num1;
        }
    }
}