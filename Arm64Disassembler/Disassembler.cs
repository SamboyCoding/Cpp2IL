namespace Arm64Disassembler;

public static class Disassembler
{
    public static List<Arm64Instruction> Disassemble(Span<byte> assembly)
    {
        var ret = new List<Arm64Instruction>();
        
        if(assembly.Length % 4 != 0)
            throw new("Arm64 instructions are 4 bytes, therefore the assembly to disassemble must be a multiple of 4 bytes");

        for (var i = 0; i < assembly.Length; i += 4)
        {
            var rawInsn = assembly.Slice(0, 4);
            
            //Assuming little endian here
            var asUint = (uint) (rawInsn[0] | (rawInsn[1] << 8) | (rawInsn[2] << 16) | (rawInsn[3] << 24));
            
            ret.Add(DisassembleSingleInstruction(asUint, i));
        }

        return ret;
    }

    public static Arm64Instruction DisassembleSingleInstruction(uint instruction, int offset = 0)
    {
        //Top bit splits into reserved/normal instruction
        var isReserved = (instruction >> 31) == 0;
        if(isReserved)
            throw new($"Encountered reserved instruction (most-significant bit not set) at offset {offset}: 0x{instruction:X}");

        //Bits 25-28 define the kind of instruction
        var type = (instruction >> 25) & 0b1111;

        if(type is 0b0001 or 0b0011)
            throw new($"Encountered unallocated instruction type (bits 25-28 are 0b0001 or 0b0011) at offset {offset}: 0x{instruction:X}");

        Arm64Instruction ret;
        
        //Further processing depends on type
        if (type == 0)
        {
            //SME (Scalable Matrix Extension, Arm V9 only)
            throw new($"SME instruction encountered at offset {offset}: 0x{instruction:X} - they are not implemented because Arm V9 is not supported");
        }
        if (type == 0b0010)
        {
            //SVE (Scalable Vector Extension, ARM v8.2)
            ret = DisassembleSveInstruction(instruction);
        } else if (type >> 1 == 0b100)
        {
            //Data processing: immediate
            ret = DisassembleImmediateDataProcessingInstruction(instruction);
        } else if (type >> 1 == 0b101)
        {
            //Branches/Exceptions/System instructions
            ret = DisassembleBranchExceptionSystemInstruction(instruction);
        } else if ((type & 0b0101) == 0b0100)
        {
            //Loads/Stores
            ret = DisassembleLoadStoreInstruction(instruction);
        }
        else
        {
            type &= 0x111; //Discard 4th bit
            ret = type == 0b101 ? DisassembleRegisterDataProcessingInstruction(instruction) : DisassembleAdvancedSimdDataProcessingInstruction(instruction);
        }

        return ret;
    }

    private static Arm64Instruction DisassembleSveInstruction(uint instruction)
    {
        var op0 = (instruction >> 29) & 0b111; //Bits 29-31
        var op1 = (instruction >> 23) & 0b11; //Bits 23-24
        var op2 = (instruction >> 17) & 0b1_1111; //Bits 17-21
        var op3 = (instruction >> 10) & 0b11_1111; //Bits 10-15
        var op4 = (instruction >> 4) & 1; //Bit 4
        
        return default;
    }
    
    private static Arm64Instruction DisassembleImmediateDataProcessingInstruction(uint instruction)
    {
        //This one, at least, is mercifully simple
        var op0 = (instruction >> 23) & 0b111; //Bits 23-25
        
        //All 7 possible mnemonics are defined.
        
        return default;
    }
    
    private static Arm64Instruction DisassembleBranchExceptionSystemInstruction(uint instruction)
    {
        var op0 = (instruction >> 29) & 0b111; //Bits 29-31
        var op1 = (instruction >> 12) & 0b11_1111_1111_1111; //Bits 12-25
        var op2 = instruction & 0b1_1111; //Bits 0-4
        
        return default;
    }
    
    private static Arm64Instruction DisassembleLoadStoreInstruction(uint instruction)
    {
        //Bits. Bits everywhere.
        var op0 = instruction >> 28; //Bits 28-31
        var op1 = (instruction >> 26) & 1; //Bit 26
        var op2 = (instruction >> 23) & 0b11; //Bits 23-24
        var op3 = (instruction >> 16) & 0b11_1111; //Bits 16-21
        var op4 = (instruction >> 10) & 0b11; //Bits 10-11

        if ((op0 & 0b11) == 0b11)
        {
            //Ignore op1
            //Simple test, if top bit of op2 is set, it's load/store reg unsigned immediate
            if ((op2 & 0b10) == 0b10)
            {
                //Load/store reg unsigned immediate
                return default;
            }
            
            //Check top bit of op3
            if((op3 & 0b10_0000) == 0b10_0000)
            {
                //One of 3
                return default;
            }
            
            //One of 4
            if (op4 == 0b11)
            {
                return Arm64LoadsStores.LoadStoreImmPreI(instruction);
            }
        }

        return default;
    }
    
    private static Arm64Instruction DisassembleRegisterDataProcessingInstruction(uint instruction)
    {
        var op0 = (instruction >> 30) & 1; //Bit 30
        var op1 = (instruction >> 28) & 1; //Bit 28
        //25-27 must be 101
        var op2 = (instruction >> 21) & 0b1111; //Bits 21-24
        var op3 = (instruction >> 10) & 0b11_1111; //Bits 10-15
        
        return default;
    }
    
    private static Arm64Instruction DisassembleAdvancedSimdDataProcessingInstruction(uint instruction)
    {
        var op0 = (instruction >> 28) & 0b1111; //Bits 28-31
        //25-27 must be 111
        var op1 = (instruction >> 23) & 0b11; //Bits 23-24
        var op2 = (instruction >> 19) & 0b1111; //Bits 19-22
        var op3 = (instruction >> 10) & 0b1_1111_1111; //Bits 10-18
        
        return default;
    }
    
}