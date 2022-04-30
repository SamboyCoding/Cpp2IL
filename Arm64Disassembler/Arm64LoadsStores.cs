namespace Arm64Disassembler;

public static class Arm64LoadsStores
{
    public static Arm64Instruction LoadStoreImmPreI(uint instruction)
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

        return default;
    }
}