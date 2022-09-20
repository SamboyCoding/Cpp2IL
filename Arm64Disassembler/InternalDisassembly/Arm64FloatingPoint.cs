namespace Arm64Disassembler.InternalDisassembly;

public static class Arm64FloatingPoint
{
    internal static Arm64Instruction ConversionToAndFromFixedPoint(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    internal static Arm64Instruction ConversionToAndFromInteger(uint instruction)
    {
        var sf = instruction.TestBit(31);
        var sFlag = instruction.TestBit(29);
        var ptype = (instruction >> 22) & 0b11;
        var rmode = (instruction >> 19) & 0b11;
        var opcode = (instruction >> 16) & 0b111;
        var rn = (int) (instruction >> 5) & 0b11111;
        var rd = (int) (instruction >> 0) & 0b11111;

        if (rmode != 0 && opcode is 0b010 or 0b011 or 0b100 or 0b101)
            throw new Arm64UndefinedInstructionException($"Floating-point conversion to/from integer: rmode != 0 not allowed with opcode 0x{opcode:X}");
        
        if(sFlag)
            throw new Arm64UndefinedInstructionException("Floating-point conversion to/from integer: S is reserved");
        
        if(ptype == 0b10 && ((opcode >> 2) == 0 || (opcode >> 1) == 0b10))
            throw new Arm64UndefinedInstructionException($"Floating-point conversion to/from integer: ptype 0b10 not allowed with opcode 0x{opcode:X}");
        
        //There are more than 3 pages of instructions here...
        //Summarising:
        //sf sets the size of the integer - 0 is 32-bit, 1 is 64-bit
        //s is unused - having it set is undefined
        //rmode is rounding mode. 0 is round to nearest (N), 1 is round to positive infinity (P), 2 is round to negative infinity (M), 3 is round to zero (Z).
        //ptype is the type of the floating point number. 0 is single, 1 is double, 2 is almost always reserved but means "top half of 128-bit", 3 is half
        //opcode is the actual operation:
        //000 - FCVT*S - Floating-point ConVert To [rmode], Signed, [rmode with ties to even for rmode = 0]
        //001 - FCVT*U - Floating-point ConVert To [rmode], Unsigned, [rmode with ties to even for rmode = 0]
        //010 - SCVTF - Signed, ConVert To Floating-point. Only defined for rmode 0
        //011 - UCVTF - Unsigned, ConVert To Floating-point. Only defined for rmode 0
        //100 - FCVT*S - Floating-point ConVert To [rmode], Signed, rmode with ties away. Only defined for rmode 0
        //101 - FCVT*U - Floating-point ConVert To [rmode], Unsigned, rmode with ties away. Only defined for rmode 0
        //110 - FMOV - floating point move without conversion, floating to integer, only defined for rmode = 0
        //111 - FMOV - floating point move without conversion, integer to floating, only defined for rmode = 0

        var mnemonic = opcode switch
        {
            0b000 or 0b100 => rmode switch
            {
                0b00 => Arm64Mnemonic.FCVTNS,
                0b01 => Arm64Mnemonic.FCVTPS,
                0b10 => Arm64Mnemonic.FCVTMS,
                0b11 => Arm64Mnemonic.FCVTZS,
                _ => throw new("Impossible rmode")
            },
            0b001 or 0b101 => rmode switch
            {
                0b00 => Arm64Mnemonic.FCVTNU,
                0b01 => Arm64Mnemonic.FCVTPU,
                0b10 => Arm64Mnemonic.FCVTMU,
                0b11 => Arm64Mnemonic.FCVTZU,
                _ => throw new("Impossible rmode")
            },
            0b010 => Arm64Mnemonic.SCVTF,
            0b011 => Arm64Mnemonic.UCVTF,
            0b110 or 0b111 => Arm64Mnemonic.FMOV,
            _ => throw new("Impossible opcode")
        };

        //FCVT* or the FMOV kind where the int is the destination
        var intRegFirst = opcode is 0b000 or 0b100 or 0b001 or 0b101 or 0b110;

        var floatingBaseReg = ptype switch
        {
            0b00 => Arm64Register.S0,
            0b01 => Arm64Register.D0,
            0b10 => Arm64Register.V0,
            0b11 => Arm64Register.H0,
            _ => throw new("Impossible ptype"),
        };

        if (floatingBaseReg == Arm64Register.V0)
        {
            if(rmode != 0b01 || opcode is not 0b110 or 0b111)
                throw new("Floating-point conversion to/from integer: ptype 0b10 only allowed with rmode 0b01 and opcode 0b110 or 0b111");
        }

        var integerBaseReg = sf ? Arm64Register.X0 : Arm64Register.W0;
        
        var regD = (intRegFirst ? integerBaseReg : floatingBaseReg) + rd;
        var regN = (intRegFirst ? floatingBaseReg : integerBaseReg) + rn;

        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Register,
            Op0Reg = regD,
            Op1Reg = regN,
        };
    }

    public static Arm64Instruction DataProcessingOneSource(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction DataProcessingTwoSource(uint instruction)
    {
        var mFlag = instruction.TestBit(31);
        var sFlag = instruction.TestBit(29);
        var pType = (instruction >> 2) & 0b11;
        var rm = (int) (instruction >> 16) & 0b1_1111;
        var opcode = (instruction >> 12) & 0b1111;
        var rn = (int) (instruction >> 5) & 0b1_1111;
        var rd = (int) (instruction >> 0) & 0b1_1111;
        
        
        if(sFlag)
            throw new Arm64UndefinedInstructionException("Floating point: Data processing 2-source: S flag is reserved");
        
        if(mFlag)
            throw new Arm64UndefinedInstructionException("Floating point: Data processing 2-source: M flag is reserved");
        
        if(opcode.TestPattern(0b1001) || opcode.TestPattern(0b1010) || opcode.TestPattern(0b1100))
            throw new Arm64UndefinedInstructionException($"Floating point: Data processing 2-source: Reserved opcode used: 0x{opcode:X}");
        
        if(pType == 0b10)
            throw new Arm64UndefinedInstructionException("Floating point: Data processing 2-source: ptype 0b10 is reserved");

        var mnemonic = opcode switch
        {
            0b0000 => Arm64Mnemonic.FMUL,
            0b0001 => Arm64Mnemonic.FDIV,
            0b0010 => Arm64Mnemonic.FADD,
            0b0011 => Arm64Mnemonic.FSUB,
            0b0100 => Arm64Mnemonic.FMAX,
            0b0101 => Arm64Mnemonic.FMIN,
            0b0110 => Arm64Mnemonic.FMAXNM,
            0b0111 => Arm64Mnemonic.FMINNM,
            0b1000 => Arm64Mnemonic.FNMUL,
            _ => throw new("Impossible opcode")
        };
        
        //ptype:
        //0 - single
        //1 - double
        //2 - reserved
        //3 - half
        
        var floatingBaseReg = pType switch
        {
            0b00 => Arm64Register.S0,
            0b01 => Arm64Register.D0,
            0b11 => Arm64Register.H0,
            _ => throw new("Impossible ptype"),
        };
        
        var regD = floatingBaseReg + rd;
        var regN = floatingBaseReg + rn;
        var regM = floatingBaseReg + rm;
        
        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Register,
            Op2Kind = Arm64OperandKind.Register,
            Op0Reg = regD,
            Op1Reg = regN,
            Op2Reg = regM,
        };
    }

    public static Arm64Instruction DataProcessingThreeSource(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction Compare(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction Immediate(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction ConditionalCompare(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction ConditionalSelect(uint instruction)
    {
        throw new NotImplementedException();
    }
}
