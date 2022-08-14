namespace Arm64Disassembler;

public static class Arm64LoadsStores
{
    public static Arm64Instruction Disassemble(uint instruction)
    {
        var op0 = instruction >> 28; //Bits 28-31
        var op1 = (instruction >> 26) & 1; //Bit 26
        var op2 = (instruction >> 23) & 0b11; //Bits 23-24
        var op3 = (instruction >> 16) & 0b11_1111; //Bits 16-21
        var op4 = (instruction >> 10) & 0b11; //Bits 10-11
        
        //As can perhaps be imagined, this is by far the most deeply-nested tree of instructions
        //At this level, despite having 5 separate operands to differentiate which path we take, most of these paths are defined by masks, not by values.
        //Unfortunately, this makes the code a bit ugly.
        
        //They are, at least, *somewhat* grouped by category using op0
        if ((op0 & 0b1011) == 0)
            //Mostly undefined instructions, but a couple of them are defined
            return DisassembleAdvancedLoadStore(instruction);

        if (op0 == 0b1101 && op1 == 0 && op2 >> 1 == 1 && op3 >> 5 == 1)
            //Literally the only concretely defined value for op0, but it still needs others to match conditions - load/store memory tags
            return DisassembleLoadStoreMemoryTags(instruction);
        
        //Five more categories for op0

        if ((op0 & 0b1011) == 0b1000)
        {
            //Load/store exclusive pair, or undefined
        }
        
        //The last 4 categories look only at the last 2 bits of op0, so we can switch now
        op0 &= 0b11;

        //Ok i lied half of these are barely grouped at all in any way that makes sense to me 
        return op0 switch
        {
            0b00 => DisassembleLoadStoreExclusiveRegOrderedOrCompareSwap(instruction), //load/store exclusive reg, load/store ordered, or compare + swap 
            0b01 => DisassembleLdAprRegisterLiteralOrMemoryCopySet(instruction), //ldapr/stlr unscaled immediate, load register literal, or memory copy/set
            0b10 => DisassembleLoadStorePairs(instruction), //actual group! load/store pairs
            0b11 => DisassembleLoadStoreRegisterOrAtomic(instruction), //various kinds of load/store register, or atomic memory operations
            _ => throw new("Loads/stores: Impossible op0 value")
        };
    }
    
    private static Arm64Instruction DisassembleAdvancedLoadStore(uint instruction)
    {
        //Most of these are actually unimplemented. Only two categories are defined, and they are both SIMD, so we can shunt over to that class.
        
        var op2 = (instruction >> 23) & 0b11; //Bits 23-24
        var op3 = (instruction >> 16) & 0b11_1111; //Bits 16-21

        if (op2 == 0b11)
            //Post-indexed simd load/store structure
            return Arm64Simd.LoadStoreSingleStructurePostIndexed(instruction);
        
        //Doesn't matter what op2 is at this point, unless the bottom 5 bits of op3 are zeroed, this is unimplemented.
        if ((op3 & 0b1_1111) == 0)
            return Arm64Simd.LoadStoreSingleStructure(instruction);
        
        throw new Arm64UndefinedInstructionException($"Advanced load/store: Congrats, you hit the minefield of undefined instructions. op2: {op2}, op3: {op3}");
    }

    private static Arm64Instruction DisassembleLoadStoreMemoryTags(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction DisassembleLoadStoreExclusiveRegOrderedOrCompareSwap(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction DisassembleLdAprRegisterLiteralOrMemoryCopySet(uint instruction)
    {
        throw new NotImplementedException();
    }

    private static Arm64Instruction DisassembleLoadStorePairs(uint instruction)
    {
        throw new NotImplementedException();
    }

    //The 'xx11' category of loads/stores
    private static Arm64Instruction DisassembleLoadStoreRegisterOrAtomic(uint instruction)
    {
        var op2 = (instruction >> 23) & 0b11; //Bits 23-24
        var op3 = (instruction >> 16) & 0b11_1111; //Bits 16-21
        var op4 = (instruction >> 10) & 0b11; //Bits 10-11
        
        //Bottom bit of op2 is irrelevant
        op2 >>= 1;
        
        if (op2 == 1)
            //Load/store reg unsigned immediate
            throw new NotImplementedException();

        //Check top bit of op3
        if (op3 >> 5 == 1)
            //Atomic, or load/store reg with non-immediate, depending on op1
            return op4 switch
            {
                0b00 => throw new NotImplementedException(), //Atomic
                0b10 => throw new NotImplementedException(), //Load/store (reg), (reg + x)
                _ => throw new NotImplementedException(), //Load store (reg), (pac)
            };

        //Some kind of load/store reg with an immediate
        return op4 switch
        {
            0b00 => throw new NotImplementedException(), //Load/store (reg), (unscaled immediate)
            0b01 => throw new NotImplementedException(), //Load/store (reg), (post-indexed immediate)
            0b10 => throw new NotImplementedException(), //Load/store (reg), (unprivileged)
            0b11 => LoadStoreRegisterFromImmPreIndexed(instruction), //Load/Store (reg), (pre-indexed immediate)
            _ => throw new("Impossible op4"),
        };
    }

    public static Arm64Instruction LoadStoreRegisterFromImmPreIndexed(uint instruction)
    {
        // Load/store immediate pre-indexed

        var size = (instruction >> 30) & 0b11; //Bits 30-31
        var v = (instruction >> 26) & 1; //Bit 26
        var opc = (instruction >> 22) & 0b11; //Bits 22-23
        var imm9 = (instruction >> 12) & 0b1_1111_1111; //Bits 12-20
        var rn = (int) (instruction >> 5) & 0b11111; //Bits 5-9
        var rt = (int) (instruction & 0b11111); //Bits 0-4

        if (size == 0b11)
        {
            //64-bit
            if (v == 0)
            {
                //Non-vector.
                //LDR/STR Xt, [Xn, #imm9]
                var isLoad = opc == 1;

                var offset = Arm64CommonUtils.SignExtend(imm9, 9, 64);

                return new Arm64Instruction
                {
                    Mnemonic = isLoad ? Arm64Mnemonic.LDR : Arm64Mnemonic.STR,
                    Op0Kind = Arm64OperandKind.Register,
                    Op1Kind = Arm64OperandKind.Memory,
                    Op0Reg = Arm64Register.X0 + rt,
                    MemBase = Arm64Register.X0 + rn,
                    MemOffset = offset,
                };
            }
        }

        throw new NotImplementedException();
    }
}