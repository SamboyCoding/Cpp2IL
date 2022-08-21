namespace Arm64Disassembler.InternalDisassembly;

public static class Arm64Branches
{
    public static Arm64Instruction ConditionalBranchImmediate(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction UnconditionalBranchImmediate(uint instruction)
    {
        var comingBack = instruction.TestBit(31);
        var imm26 = instruction & ((1 << 26) - 1);

        imm26 <<= 2; // Multiply by 4 because jump dest has to be aligned anyway

        return new()
        {
            Mnemonic = comingBack ? Arm64Mnemonic.BL : Arm64Mnemonic.B,
            Op0Kind = Arm64OperandKind.ImmediatePcRelative,
            Op0Imm = imm26,
        };
    }

    public static Arm64Instruction TestAndBranch(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction CompareAndBranch(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction UnconditionalBranchRegister(uint instruction)
    {
        var opc = (instruction >> 21) & 0b1111; //Bits 21-24
        var op2 = (instruction >> 16) & 0b1_1111; //Bits 16-20
        var op3 = (instruction >> 10) & 0b1_1111; //Bits 10-15
        var rn = (int) (instruction >> 5) & 0b1_1111; //Bits 5-9
        var op4 = instruction & 0b1_1111; //Bits 0-4
        
        if(op2 != 0b11111)
            throw new Arm64UndefinedInstructionException($"Unconditional Branch: op2 != 0b11111: {op2:X}");

        if (opc == 0b0010)
        {
            if (op3 == 0)
            {
                //ret. but sanity check op4
                if(op4 != 0)
                    throw new Arm64UndefinedInstructionException($"Ret instruction with op4 != 0: {op4:X}");
                
                //By default, ret returns to the caller, the address of which is in X30, however X30 can supposedly be overriden by providing a register in rn.
                //Looks like most compilers pass 30 anyway - I don't even know what the value being "absent" would imply, as presumably 0 (= X0) is valid? Perhaps 31 (=WZR/SP) is absent?
                if (rn == 0)
                    rn = Arm64Register.X30 - Arm64Register.X0;

                return new()
                {
                    Mnemonic = Arm64Mnemonic.RET,
                    Op0Kind = Arm64OperandKind.Register,
                    Op0Reg = Arm64Register.X0 + rn
                };
            }
        }
        
        throw new NotImplementedException();
    }
}