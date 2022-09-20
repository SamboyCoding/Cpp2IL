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
            case 0b0100 when op1Hi == 0 && (op2 & 0b111) == 0b101 && (op3 & 0b1_1000_0011) == 0b0_0000_0010:
                return CryptoAes(instruction);
            case 0b0101 when op1Hi == 0 && (op2 & 0b111) == 0b101:
                return CryptoTwoRegSha(instruction);
            case 0b0101 when op1Hi == 0 && (op2 & 0b0100) == 0:
                return CryptoThreeRegSha(instruction);
            case 0b0101 or 0b0111 when op1 == 0 && (op2 >> 2) == 0:
                return AdvancedSimdScalarCopy(instruction);
        }

        //Masks for op0
        if (op0.TestPattern(0b1001, 0))
        {
            //0xx0 family: Advanced SIMD (non-scalar)
            return Arm64NonScalarAdvancedSimd.Disassemble(instruction);
        }

        if (op0.TestPattern(0b1101, 0b0101))
        {
            //01x1 family: Advanced SIMD (scalar)
            return Arm64ScalarAdvancedSimd.Disassemble(instruction);
        }

        if (op0 == 0b1100)
        {
            throw new NotImplementedException($"SIMD: Unimplemented 1100 family: Op1: {op1}, Op2: {op2}, Op3: {op3}");
        }

        if (op0.TestPattern(0b0101, 0b0001))
        {
            //x0x1: Floating point family - either conversion two/from integer/fixed-point, or some general floating-point instruction
            
            if (op1.TestBit(1))
                //Only one with bit 24 set
                return Arm64FloatingPoint.DataProcessingThreeSource(instruction);

            //Get the two conversion types out first
            
            if (!op2.TestBit(2))
                //Only one with bit 20 clear
                return Arm64FloatingPoint.ConversionToAndFromFixedPoint(instruction);

            if ((op3 & 0b11_1111) == 0)
                return Arm64FloatingPoint.ConversionToAndFromInteger(instruction);

            if ((op3 & 0b1_1111) == 0b1_0000)
                return Arm64FloatingPoint.DataProcessingOneSource(instruction);
            
            if((op3 & 0b1111) == 0b1000)
                return Arm64FloatingPoint.Compare(instruction);

            if ((op3 & 0b111) == 0b100)
                return Arm64FloatingPoint.Immediate(instruction);

            return (op3 & 0b11) switch
            {
                0b01 => Arm64FloatingPoint.ConditionalCompare(instruction),
                0b10 => Arm64FloatingPoint.DataProcessingTwoSource(instruction),
                0b11 => Arm64FloatingPoint.ConditionalSelect(instruction),
                _ => throw new("Impossible op3"),
            };
        }

        throw new NotImplementedException($"Unimplemented SIMD instruction. Op0: {op0}, Op1: {op1}, Op2: {op2}, Op3: {op3}");
    }


    private static Arm64Instruction CryptoAes(uint instruction)
    {
        var size = (instruction >> 22) & 0b11;
        var opcode = (instruction >> 12) & 0b1_1111;
        var rn = (int) (instruction >> 5) & 0b11111;
        var rd = (int) instruction & 0b11111;
        
        if(size != 0)
            throw new Arm64UndefinedInstructionException("AES instruction with size != 0");

        if (opcode.TestBit(3) || instruction.TestBit(4) || (instruction >> 2) == 0)
            throw new Arm64UndefinedInstructionException($"AES: Reserved opcode 0x{opcode:X}");

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

    internal static Arm64Instruction LoadStoreSingleStructure(uint instruction)
    {
        throw new NotImplementedException();
    }

    internal static Arm64Instruction LoadStoreSingleStructurePostIndexed(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    public static Arm64Instruction AdvancedSimdScalarCopy(uint instruction)
    {
        throw new NotImplementedException();
    }
}
