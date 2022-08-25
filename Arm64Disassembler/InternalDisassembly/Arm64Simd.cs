namespace Arm64Disassembler.InternalDisassembly;

public static class Arm64Simd
{
    //I hate this entire table.
    
    
    public static Arm64Instruction Disassemble(uint instruction)
    {
        var op0 = (instruction >> 28) & 0b1111; //Bits 28-31
        //25-27 must be 111
        var op1 = (instruction >> 23) & 0b11; //Bits 23-24
        var op2 = (instruction >> 19) & 0b1111; //Bits 19-22
        var op3 = (instruction >> 10) & 0b1_1111_1111; //Bits 10-18

        var op1Hi = op1 >> 1;

        //Concrete values or one-masked-bit for op0
        switch (op0)
        {
            case 0b0100 when op1Hi == 0 && (op2 & 0b111) == 0b101:
                return CryptoAes(instruction);
            case 0b0101 when op1Hi == 0 && (op2 & 0b111) == 0b101:
                return CryptoTwoRegSha(instruction);
            case 0b0101 when op1Hi == 0 && (op2 & 0b0100) == 0:
                return CryptoThreeRegSha(instruction);
            case 0b0101 or 0b0111 when op1 == 0 && (op2 >> 2) == 0:
                return AdvancedSimdScalarCopy(instruction);
        }

        //Masks for op0
        if ((op0 & 0b1001) == 0)
        {
            //0xx0 family: Advanced SIMD (non-scalar)
            return AdvancedSimdNonScalar(instruction);
        }

        throw new NotImplementedException($"Unimplemented SIMD instruction. Op0: {op0}, Op1: {op1}, Op2: {op2}, Op3: {op3}");
    }

    private static Arm64Instruction AdvancedSimdNonScalar(uint instruction)
    {
        var op0 = (instruction >> 28) & 0b1111;
        var op1 = (instruction >> 23) & 0b11;
        var op2 = (instruction >> 19) & 0b1111;
        var op3 = (instruction >> 10) & 0b1_1111_1111;

        var op1Hi = (op1 >> 1) == 1;
        var op2UpperHalf = op2 >> 2;
        var op3Lo = (op3 & 1) == 1;

        if (op1 == 0b11)
            throw new Arm64UndefinedInstructionException("Advanced SIMD instruction with op1 == 11");

        //Handle the couple of cases where op1 is not simply 0b0x
        if (op1 == 0b10)
            return op3Lo
                ? op2 == 0 ? AdvancedSimdModifiedImmediate(instruction) : AdvancedSimdShiftByImmediate(instruction)
                : AdvancedSimdVectorXIndexedElement(instruction);

        if (op1 == 0 && op2UpperHalf == 0 && (op3 & 0b100001) == 1)
            return AdvancedSimdCopy(instruction);

        if ((op0 & 0b1011) == 0 && !op1Hi && (op2UpperHalf & 1) == 0)
        {
            var test = op3 & 0b100011;
            if (test == 0)
                return AdvancedSimdTableLookup(instruction);
            if (test == 0b10)
                return AdvancedSimdPermute(instruction);
        }
        
        if((op0 & 0b1011) == 0b10 && !op1Hi && (op2UpperHalf & 1) == 0)
        {
            if ((op3 & 0b100001) == 0)
                return AdvancedSimdExtract(instruction);
        }
        
        if(op1 == 0 && op2UpperHalf == 0 && (op3 & 0b100001) == 1)
            return AdvancedSimdCopy(instruction);

        //Ok, now all the remaining define op0 as 0xx0 and op1 as 0x so there is no point checking either

        if (op2UpperHalf == 0b10 && (op3 & 0b110001) == 1)
            return AdvancedSimdThreeSameFp16(instruction);

        if (op2 == 0b1111 && (op3 & 0b1_1000_0011) == 0b10)
            return AdvancedSimdTwoRegisterMiscFp16(instruction);
        
        if((op2UpperHalf & 1) == 0 && (op3 * 0b100001) == 0b100001)
            return AdvancedSimdThreeRegExtension(instruction);

        if (op2 is 0b0100 or 0b1100 && (op3 & 0b110000011) == 0b10)
            return AdvancedSimdTwoRegisterMisc(instruction);

        if (op2 is 0b0110 or 0b1110 && (op3 & 0b110000011) == 0b10)
            return AdvancedSimdAcrossLanes(instruction);

        if ((op2UpperHalf & 1) == 1)
        {
            if ((op3 & 0b11) == 0)
                return AdvancedSimdThreeDifferent(instruction);

            if (op3Lo)
                return AdvancedSimdThreeSame(instruction);
        }

        throw new Arm64UndefinedInstructionException($"Advanced SIMD instruction (non-scalar): op0: {op0}, op1: {op1}, op2: {op2}, op3: {op3}");
    }

    private static Arm64Instruction CryptoAes(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction CryptoTwoRegSha(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction CryptoThreeRegSha(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction AdvancedSimdScalarCopy(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction AdvancedSimdModifiedImmediate(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction AdvancedSimdShiftByImmediate(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction AdvancedSimdVectorXIndexedElement(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction AdvancedSimdCopy(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction AdvancedSimdTableLookup(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction AdvancedSimdPermute(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction AdvancedSimdExtract(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction AdvancedSimdThreeSameFp16(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction AdvancedSimdTwoRegisterMiscFp16(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction AdvancedSimdThreeRegExtension(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction AdvancedSimdTwoRegisterMisc(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction AdvancedSimdAcrossLanes(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction AdvancedSimdThreeDifferent(uint instruction)
    {
        var q = instruction.TestBit(30);
        var u = instruction.TestBit(29);
        var size = (instruction >> 22) & 0b11;
        var rm = (int) (instruction >> 16) & 0b1_1111;
        var opcode = (instruction >> 12) & 0b1111;
        var rn = (int) (instruction >> 5) & 0b1_1111;
        var rd = (int) instruction & 0b1_1111;
        
        if(opcode == 0b1111)
            throw new Arm64UndefinedInstructionException("AdvancedSimdThreeSame: opcode == 1111");

        if (size == 0b11)
            throw new Arm64UndefinedInstructionException("AdvancedSimdThreeSame: size = 11");

        Arm64Mnemonic mnemonic;
        if (u)
            mnemonic = opcode switch
            {
                0b0000 when q => Arm64Mnemonic.UADDL2,
                0b0000 => Arm64Mnemonic.UADDL,
                0b0001 when q => Arm64Mnemonic.UADDW2,
                0b0001 => Arm64Mnemonic.UADDW,
                0b0010 when q => Arm64Mnemonic.USUBL2,
                0b0010 => Arm64Mnemonic.USUBL,
                0b0011 when q => Arm64Mnemonic.USUBW2,
                0b0011 => Arm64Mnemonic.USUBW,
                0b0100 when q => Arm64Mnemonic.RADDHN2,
                0b0100 => Arm64Mnemonic.RADDHN,
                0b0101 when q => Arm64Mnemonic.UABAL2,
                0b0101 => Arm64Mnemonic.UABAL,
                0b0110 when q => Arm64Mnemonic.RSUBHN2,
                0b0110 => Arm64Mnemonic.RSUBHN,
                0b0111 when q => Arm64Mnemonic.UABDL2,
                0b0111 => Arm64Mnemonic.UABDL,
                0b1000 when q => Arm64Mnemonic.UMLAL2,
                0b1000 => Arm64Mnemonic.UMLAL,
                0b1001 => throw new Arm64UndefinedInstructionException("AdvancedSimdThreeSame: U && opcode == 1001"),
                0b1010 when q => Arm64Mnemonic.UMLSL2,
                0b1010 => Arm64Mnemonic.UMLSL,
                0b1011 => throw new Arm64UndefinedInstructionException("AdvancedSimdThreeSame: U && opcode == 1011"),
                0b1100 when q => Arm64Mnemonic.UMULL2,
                0b1100 => Arm64Mnemonic.UMULL,
                0b1101 => throw new Arm64UndefinedInstructionException("AdvancedSimdThreeSame: U && opcode == 1101"),
                0b1110 => throw new Arm64UndefinedInstructionException("AdvancedSimdThreeSame: U && opcode == 1110"),
                _ => throw new("Impossible opcode")
            };
        else
            mnemonic = opcode switch
            {
                0b0000 when q => Arm64Mnemonic.SADDL2,
                0b0000 => Arm64Mnemonic.SADDL,
                0b0001 when q => Arm64Mnemonic.SADDW2,
                0b0001 => Arm64Mnemonic.SADDW,
                0b0010 when q => Arm64Mnemonic.SSUBL2,
                0b0010 => Arm64Mnemonic.SSUBL,
                0b0011 when q => Arm64Mnemonic.SSUBW2,
                0b0011 => Arm64Mnemonic.SSUBW,
                0b0100 when q => Arm64Mnemonic.ADDHN2,
                0b0100 => Arm64Mnemonic.ADDHN,
                0b0101 when q => Arm64Mnemonic.SABAL2,
                0b0101 => Arm64Mnemonic.SABAL,
                0b0110 when q => Arm64Mnemonic.SUBHN2,
                0b0110 => Arm64Mnemonic.SUBHN,
                0b0111 when q => Arm64Mnemonic.SABDL2,
                0b0111 => Arm64Mnemonic.SABDL,
                0b1000 when q => Arm64Mnemonic.SMLAL2,
                0b1000 => Arm64Mnemonic.SMLAL,
                0b1001 when q => Arm64Mnemonic.SQDMLAL2,
                0b1001 => Arm64Mnemonic.SQDMLAL,
                0b1010 when q => Arm64Mnemonic.SMLSL2,
                0b1010 => Arm64Mnemonic.SMLSL,
                0b1011 when q => Arm64Mnemonic.SQDMLSL2,
                0b1011 => Arm64Mnemonic.SQDMLSL,
                0b1100 when q => Arm64Mnemonic.SMULL2,
                0b1100 => Arm64Mnemonic.SMULL,
                0b1101 when q => Arm64Mnemonic.SQDMULL2,
                0b1101 => Arm64Mnemonic.SQDMULL,
                0b1110 when q => Arm64Mnemonic.PMULL2,
                0b1110 => Arm64Mnemonic.PMULL,
                _ => throw new("Impossible opcode")
            };

        var baseReg = Arm64Register.V0;
        var sizeOne = size switch
        {
            0b00 => Arm64ArrangementSpecifier.EightH,
            0b01 => Arm64ArrangementSpecifier.FourS,
            0b10 => Arm64ArrangementSpecifier.TwoD,
            _ => throw new("Impossible size"),
        };

        var sizeTwo = size switch
        {
            0b00 when q => Arm64ArrangementSpecifier.EightB,
            0b00 => Arm64ArrangementSpecifier.SixteenB,
            0b01 when q => Arm64ArrangementSpecifier.FourH,
            0b01 => Arm64ArrangementSpecifier.EightH,
            0b10 when q => Arm64ArrangementSpecifier.TwoS,
            0b10 => Arm64ArrangementSpecifier.FourS,
            _ => throw new("Impossible size"),
        };

        return new Arm64Instruction()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Register,
            Op2Kind = Arm64OperandKind.Register,
            Op0Reg = baseReg + rd,
            Op1Reg = baseReg + rn,
            Op2Reg = baseReg + rm,
            Op0Arrangement = sizeOne,
            Op1Arrangement = sizeTwo,
            Op2Arrangement = sizeTwo,
        };
    }

    private static Arm64Instruction AdvancedSimdThreeSame(uint instruction)
    {
        throw new NotImplementedException();
    }

    internal static Arm64Instruction LoadStoreSingleStructure(uint instruction)
    {
        throw new NotImplementedException();
    }

    internal static Arm64Instruction LoadStoreSingleStructurePostIndexed(uint instruction)
    {
        throw new NotImplementedException();
    }
}
