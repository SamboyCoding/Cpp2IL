namespace Arm64Disassembler;

public static class Arm64Branches
{
    public static Arm64Instruction ConditionalBranchImmediate(uint instruction)
    {
        return default;
    }

    public static Arm64Instruction UnconditionalBranchImmediate(uint instruction)
    {
        return default;
    }

    public static Arm64Instruction TestAndBranch(uint instruction)
    {
        return default;
    }

    public static Arm64Instruction CompareAndBranch(uint instruction)
    {
        return default;
    }

    public static Arm64Instruction UnconditionalBranchRegister(uint instruction)
    {
        var opc = (instruction >> 21) & 0b1111; //Bits 21-24
        var op2 = (instruction >> 16) & 0b1_1111; //Bits 16-20
        var op3 = (instruction >> 10) & 0b1_1111; //Bits 10-15
        var rn = (int) (instruction >> 5) & 0b1_1111; //Bits 5-9
        var op4 = instruction & 0b1_1111; //Bits 0-4
        
        if(op2 != 0b11111)
            throw new("Unconditional Branch: op2 != 0b11111 is unallocated");

        if (opc == 0b0010)
        {
            if (op3 == 0)
            {
                //ret. but sanity check op4
                if(op4 != 0)
                    throw new("Ret instruction with op4 != 0 is unallocated");
                
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
        
        return default;
    }
}