namespace Arm64Disassembler.InternalDisassembly;

//Advanced SIMD family where op0 is 01x1
public static class Arm64ScalarAdvancedSimd
{
    public static Arm64Instruction Disassemble(uint instruction)
    {
        var op1 = (instruction >> 23) & 0b11; //Bits 23-24
        var op2 = (instruction >> 19) & 0b1111; //Bits 19-22
        var op3 = (instruction >> 10) & 0b1_1111_1111; //Bits 10-18

        if (op1 == 0 && (op2 >> 2) == 0 && op3.TestPattern(0b000100001, 0b1))
            return Copy(instruction);

        if (op1 is 0b10 or 0b11)
        {
            if (op3.TestBit(0))
            {
                if (op1.TestBit(0))
                    //Op1 11 and op3 ends with a 1
                    throw new Arm64UndefinedInstructionException("Advanced SIMD (scalar): Unallocated");

                return ShiftByImmediate(instruction);
            }

            //Scalar x indexed element
            return ScalarXIndexedElement(instruction);
        }

        //This leaves the largest group, op1 == 0b0x
        //Switch by op2 first, where we can:

        if (op2 == 0b1111)
        {
            if (!op3.TestPattern(0b110000011, 0b10))
                throw new Arm64UndefinedInstructionException("Advanced SIMD (scalar): Unallocated");

            return TwoRegisterMiscFp16(instruction);
        }

        if (op2.TestBit(2))
        {
            //x1xx
            //Check to exclude x100 and x110 first
            if (op2.TestPattern(0b0111, 0b0100) && op3.TestPattern(0b110000011, 0b10))
                return TwoRegisterMisc(instruction);

            if (op2.TestPattern(0b0111, 0b0110) && op3.TestPattern(0b110000011, 0b10))
                return Pairwise(instruction);

            //Remaining x1xx
            if (op3.TestPattern(0b11, 0))
                return ThreeDifferent(instruction);

            if (op3.TestBit(0))
                return ThreeSame(instruction);

            throw new Arm64UndefinedInstructionException($"Advanced SIMD (scalar): Unallocated x1xx family: op2 = 0x{op2:X2}, op3 = {op3:X3}");
        }

        if (op2.TestPattern(0b1100, 0b1000) && op3.TestPattern(0b000110001, 1))
            return ThreeSameFp16(instruction);

        if (op2.TestPattern(0b0100, 0) && op3.TestPattern(0b000100001))
            return ThreeSameExtra(instruction);

        throw new Arm64UndefinedInstructionException($"Advanced SIMD (scalar): Unallocated fall-through, op1 = 0x{op1:X2}, op2 = 0x{op2:X2}, op3 = {op3:X3}");
    }

    public static Arm64Instruction Copy(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction ShiftByImmediate(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction ScalarXIndexedElement(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction TwoRegisterMiscFp16(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction TwoRegisterMisc(uint instruction)
    {
        var uFlag = instruction.TestBit(29);
        var size = (instruction >> 22) & 0b11; //Bits 22-23
        var opcode = (instruction >> 12) & 0b1_1111; //Bits 12-16
        var rn = (int) (instruction >> 5) & 0b1_1111; //Bits 5-9
        var rd = (int) instruction & 0b1_1111; //Bits 0-4

        var sz = size.TestBit(0);
        
        //This is almost excessively miscellaneous.
        //Almost everything here has to be handled case-by-case.

        Arm64Register baseReg;
        Arm64Mnemonic mnemonic;

        switch (opcode)
        {
            case 0b11101 when !uFlag && size is 0b00 or 0b01:
                baseReg = sz ? Arm64Register.D0 : Arm64Register.S0; //32 << sz
                mnemonic = Arm64Mnemonic.SCVTF;
                break;
            default:
                throw new NotImplementedException();
        }

        var regD = baseReg + rd;
        var regN = baseReg + rn;

        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Register,
            Op0Reg = regD,
            Op1Reg = regN
        };
    }
    
    public static Arm64Instruction Pairwise(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction ThreeDifferent(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction ThreeSame(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction ThreeSameFp16(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction ThreeSameExtra(uint instruction)
    {
        throw new NotImplementedException();
    }
}
