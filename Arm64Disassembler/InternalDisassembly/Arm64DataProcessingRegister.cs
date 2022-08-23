namespace Arm64Disassembler.InternalDisassembly;

public static class Arm64DataProcessingRegister
{
    public static Arm64Instruction Disassemble(uint instruction)
    {
        var op0 = instruction.TestBit(30); //Bit 30
        var op1 = instruction.TestBit(28); //Bit 28
        //25-27 must be 101
        var op2 = (instruction >> 21) & 0b1111; //Bits 21-24
        var op3 = (instruction >> 10) & 0b11_1111; //Bits 10-15

        if (op2 == 0b0110 && op1)
            return op0
                ? DataProcessing1Source(instruction)
                : DataProcessing2Source(instruction);

        if (!op1)
        {
            if (op2 >> 3 == 0)
                return LogicalShiftedRegister(instruction);
            
            if((op2 & 0b1001) == 0b1000)
                return AddSubtractShiftedRegister(instruction);
            
            return AddSubtractExtendedRegister(instruction);
        }

        return op2 switch
        {
            0b0000 when op3 == 0 => AddSubtractWithCarry(instruction),
            0b0000 when op3 is 0b100001 or 0b000001 => RotateRightIntoFlags(instruction),
            0b0000 when (op3 & 0b1111) == 0b0010 => EvaluateIntoFlags(instruction),
            0b0010 when op3.TestBit(1) => ConditionalCompareRegister(instruction),
            0b0010 => ConditionalCompareImmediate(instruction),
            0b0100 => ConditionalSelect(instruction),
            _ => DataProcessing3Source(instruction)
        };
    }

    private static Arm64Instruction DataProcessing1Source(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction DataProcessing2Source(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction LogicalShiftedRegister(uint instruction)
    {
        var is64Bit = instruction.TestBit(31); //sf flag
        var opc = (instruction >> 29) & 0b11;
        var shift = (instruction >> 22) & 0b11;
        var negateFlag = instruction.TestBit(21); //N flag - defines if the result is negated
        var rm = (int) (instruction >> 16) & 0b1_1111;
        var imm6 = (instruction >> 10) & 0b11_1111;
        var rn = (int) (instruction >> 5) & 0b1_1111;
        var rd = (int) instruction & 0b1_1111;
        
        if(!is64Bit && imm6.TestBit(5))
            throw new Arm64UndefinedInstructionException("LogicalShiftedRegister: imm6 bit 5 set and sf = 0");

        var opcode = opc switch
        {
            0b00 when negateFlag => Arm64Mnemonic.BIC,
            0b00 => Arm64Mnemonic.AND,
            0b01 when negateFlag => Arm64Mnemonic.ORN,
            0b01 => Arm64Mnemonic.ORR,
            0b10 when negateFlag => Arm64Mnemonic.EON,
            0b10 => Arm64Mnemonic.EOR,
            0b11 when negateFlag => Arm64Mnemonic.BICS,
            0b11 => Arm64Mnemonic.ANDS,
            _ => throw new("LogicalShiftedRegister: impossible opc")
        };

        var baseReg = is64Bit ? Arm64Register.X0 : Arm64Register.W0;
        var regD = baseReg + rd;
        var regN = baseReg + rn;
        var regM = baseReg + rm;


        return new()
        {
            Mnemonic = opcode,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Register,
            Op2Kind = Arm64OperandKind.Register,
            Op3Kind = Arm64OperandKind.Immediate,
            Op0Reg = regD,
            Op1Reg = regN,
            Op2Reg = regM,
            Op3Imm = imm6,
        };
    }
    
    private static Arm64Instruction AddSubtractShiftedRegister(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction AddSubtractExtendedRegister(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction AddSubtractWithCarry(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction RotateRightIntoFlags(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction EvaluateIntoFlags(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction ConditionalCompareRegister(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction ConditionalCompareImmediate(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction ConditionalSelect(uint instruction)
    {
        throw new NotImplementedException();
    }
    
    private static Arm64Instruction DataProcessing3Source(uint instruction)
    {
        var is64Bit = instruction.TestBit(31);
        var op54 = (instruction >> 29) & 0b11;
        var op31 = (instruction >> 21) & 0b111;
        var rm = (int) (instruction >> 16) & 0b1_1111;
        var o0 = instruction.TestBit(15);
        var ra = (int) (instruction >> 10) & 0b1_1111;
        var rn = (int) (instruction >> 5) & 0b1_1111;
        var rd = (int) instruction & 0b1_1111;
        
        if(op54 != 0)
            throw new Arm64UndefinedInstructionException("DataProcessing3Source: op54 != 0");

        var mnemonic = op31 switch
        {
            0b000 when o0 => Arm64Mnemonic.MSUB,
            0b000 => Arm64Mnemonic.MADD,
            0b001 when !is64Bit => throw new Arm64UndefinedInstructionException("DataProcessing3Source: op31 == 0b001 && sf == 0"),
            0b001 when o0 =>  Arm64Mnemonic.SMSUBL,
            0b001 => Arm64Mnemonic.SMADDL,
            0b010 when !o0 && is64Bit => Arm64Mnemonic.SMULH,
            0b101 when o0 && is64Bit => Arm64Mnemonic.UMSUBL,
            0b101 when !o0 && is64Bit => Arm64Mnemonic.UMADDL,
            0b110 when o0 && is64Bit => Arm64Mnemonic.UMULH,
            _ => throw new Arm64UndefinedInstructionException($"DataProcessing3Source: unallocated operand combination: op31 = {op31} o0 = {o0} sf = {(is64Bit ? 1 : 0)}")
        };
        
        var baseReg = is64Bit ? Arm64Register.X0 : Arm64Register.W0;
        
        var regM = baseReg + rm;
        var regN = baseReg + rn;
        var regD = baseReg + rd;
        var regA = baseReg + ra;

        return new()
        {
            Mnemonic = mnemonic,
            Op0Kind = Arm64OperandKind.Register,
            Op1Kind = Arm64OperandKind.Register,
            Op2Kind = Arm64OperandKind.Register,
            Op3Kind = Arm64OperandKind.Register,
            Op0Reg = regD,
            Op1Reg = regN,
            Op2Reg = regM,
            Op3Reg = regA,
        };
    }
}