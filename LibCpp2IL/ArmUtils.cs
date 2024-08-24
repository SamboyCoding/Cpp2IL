namespace LibCpp2IL;

public static class ArmUtils
{
    public const uint PC_REG = 15;

    public static (uint register, ushort immediateValue) GetOperandsForLiteralLdr(uint inst)
    {
        if (inst.Bits(16, 16) != 0b_1110_0101_1001_1111)
            return (0, 0);

        return (inst.Bits(12, 4), (ushort)inst.Bits(0, 12));
    }

    public static (uint firstReg, uint secondReg, uint thirdReg) GetOperandsForRegisterLdr(uint inst)
    {
        if (inst.Bits(20, 12) != 0b_1110_0111_1001)
            return (0, 0, 0);

        //Ldr t, n, m
        var reg_n = inst.Bits(16, 4);
        var reg_t = inst.Bits(12, 4);
        var reg_m = inst.Bits(0, 4);
        return (reg_t, reg_n, reg_m);
    }

    public static (uint firstReg, uint secondReg, uint thirdReg) GetOperandsForRegisterAdd(uint inst)
    {
        if (inst.Bits(21, 11) != 0b_1110_0000_100)
            return (0, 0, 0);

        var reg_d = inst.Bits(12, 4);
        var reg_n = inst.Bits(16, 4);
        var reg_m = inst.Bits(0, 4);
        return (reg_d, reg_n, reg_m);
    }
}
