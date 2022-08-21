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
        var isNegated = instruction.TestBit(24);
        var imm14 = (instruction >> 5) & 0b11_1111_1111_1111;
        var rt = (int)(instruction & 0b1_1111);
        var b5 = instruction.TestBit(31);
        var b40 = (instruction >> 19) & 0b1_1111;

        var mnemonic = isNegated ? Arm64Mnemonic.TBNZ : Arm64Mnemonic.TBZ;

        var bitToTest = b40;
        if (b5)
            bitToTest &= 1 << 5;

        var jumpTo = Arm64CommonUtils.CorrectSignBit(imm14, 14) * 4;
        var regT = Arm64Register.X0 + rt;

        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Immediate,
            Op2Kind = Arm64OperandKind.ImmediatePcRelative,
            Op0Reg = regT,
            Op1Imm = bitToTest,
            Op2Imm = jumpTo,
        };
    }

    public static Arm64Instruction CompareAndBranch(uint instruction)
    {
        var is64Bit = instruction.TestBit(31); //sf flag
        var isNegated = instruction.TestBit(24);
        var imm19 = (instruction >> 5) & ((1 << 19) - 1);
        var rt = (int)(instruction & 0b1_1111);

        var mnemonic = isNegated ? Arm64Mnemonic.CBNZ : Arm64Mnemonic.CBZ;
        var baseReg = is64Bit ? Arm64Register.X0 : Arm64Register.W0;

        var immediate = Arm64CommonUtils.CorrectSignBit(imm19, 19) * 4;
        var regT = baseReg + rt;

        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.ImmediatePcRelative,
            Op0Reg = regT,
            Op1Imm = immediate,
        };
    }

    public static Arm64Instruction UnconditionalBranchRegister(uint instruction)
    {
        //This is by far the most cursed instruction table in the specification. 90% of it is unallocated.
        
        var opc = (instruction >> 21) & 0b1111; //Bits 21-24
        var op2 = (instruction >> 16) & 0b1_1111; //Bits 16-20
        var op3 = (instruction >> 10) & 0b1_1111; //Bits 10-15
        var rn = (int) (instruction >> 5) & 0b1_1111; //Bits 5-9
        var op4 = instruction & 0b1_1111; //Bits 0-4
        
        //opc:
        //  0000 - some variant of BR - no modifier variant
        //  0001 - some variant of BL - no modifier variant 
        //  0010 - some variant of RET
        //  0011 - unallocated
        //  0100 - some variant of ERET
        //  0101 - some variant of DRPS
        //  011x - unallocated
        //  1000 - some variant of BR - register modifier variant
        //  1001 - some variant of BL - register modifier variant
        //  11xx - unallocated
        
        if(op2 != 0b11111)
            throw new Arm64UndefinedInstructionException($"Unconditional Branch: op2 != 0b11111: {op2:X}");

        return opc switch
        {
            0b0011 or 0b0110 or 0b0111 or 0b1100 or 0b1101 or 0b1110 or 0b1111 => throw new Arm64UndefinedInstructionException($"Unconditional Branch: Unallocated opc: {opc}"),
            0b0000 or 0b1000 => HandleBrFamily(instruction),
            0b0001 or 0b1001 => HandleBlFamily(instruction),
            0b0010 => HandleRetFamily(instruction),
            0b0100 => HandleEretFamily(instruction),
            0b0101 => HandleDrpsFamily(instruction),
            _ => throw new($"Impossible opc: {opc}")
        };
    }

    private static Arm64Instruction HandleRetFamily(uint instruction)
    {
        var op3 = (instruction >> 10) & 0b1_1111; //Bits 10-15
        var rn = (int) (instruction >> 5) & 0b1_1111; //Bits 5-9
        var op4 = instruction & 0b1_1111; //Bits 0-4
        
        
        //TODO See if we can clean this up and implement the rest (retaa, retab)
        if (op3 == 0)
        {
            //ret. but sanity check op4
            if (op4 == 0)
            {
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

    private static Arm64Instruction HandleBrFamily(uint instruction)
    {
        var opc = (instruction >> 21) & 0b1111; //Bits 21-24
        var op3 = (instruction >> 10) & 0b1_1111; //Bits 10-15
        var rn = (int) (instruction >> 5) & 0b1_1111; //Bits 5-9
        var op4 = instruction & 0b1_1111; //Bits 0-4
        
        if(opc is not 0b0000)
            //TODO 1000 family - BRAA etc
            throw new NotImplementedException();

        if (op3 is 0 && op4 is 0)
        {
            //Simple BR
            return new()
            {
                Mnemonic = Arm64Mnemonic.BR,
                Op0Kind = Arm64OperandKind.Register,
                Op0Reg = Arm64Register.X0 + rn
            };
        }

        if (op3 is 0b000010 && op4 is 0b11111)
        {
            //BRAA family, key a, zero modifier
            throw new NotImplementedException();
        }

        if (op3 is 0b000011 && op4 is 0b11111)
        {
            //BRAA family, key b, zero modifier
            throw new NotImplementedException();
        }

        throw new Arm64UndefinedInstructionException($"BR Family: op3 {op3}, op4 {op4}");
    }
    
    private static Arm64Instruction HandleBlFamily(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction HandleEretFamily(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction HandleDrpsFamily(uint instruction)
    {
        throw new NotImplementedException();
    }
}