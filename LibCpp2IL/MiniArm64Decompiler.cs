using System.Collections.Generic;
using LibCpp2IL.Elf;

namespace LibCpp2IL;

/// <summary>
/// Full credit for most of this file goes to djKaty in the il2cppinspector project.
/// </summary>
internal static class MiniArm64Decompiler
{
    private static (uint reg, ulong page)? GetAdrp(uint inst, ulong pc)
    {
        if ((inst.Bits(24, 8) & 0b_1000_1111) != 1 << 7)
            return null;

        var addendLo = inst.Bits(29, 2);
        var addendHi = inst.Bits(5, 19);
        var addend = (addendHi << 14) + (addendLo << 12);
        var page = pc & ~((1Lu << 12) - 1);
        var reg = inst.Bits(0, 5);

        return (reg, page + addend);
    }

    // https://static.docs.arm.com/100878/0100/fundamentals_of_armv8_a_100878_0100_en.pdf states:
    // Unlike ARMv7-A, there is no implied offset of 4 or 8 bytes
    private static (uint reg, ulong addr)? GetAdr(uint inst, ulong pc)
    {
        if (inst.Bits(24, 5) != 0b10000 || inst.Bits(31, 1) != 0)
            return null;

        ulong imm = (inst.Bits(5, 19) << 2) + inst.Bits(29, 2);

        // Sign extend the 21-bit number to 64 bits
        imm = (imm & (1 << 20)) == 0 ? imm : imm | unchecked((ulong)-(1 << 21));

        var reg = inst.Bits(0, 5);

        return (reg, pc + imm);
    }

    private static (uint reg_n, uint reg_d, uint imm)? GetAdd64(uint inst)
    {
        if (inst.Bits(22, 10) != 0b_1001_0001_00)
            return null;

        var imm = inst.Bits(10, 12);
        var regN = inst.Bits(5, 5);
        var regD = inst.Bits(0, 5);

        return (regN, regD, imm);
    }

    private static (uint reg_t, uint reg_n, uint simm)? GetLdr64ImmOffset(uint inst)
    {
        if (inst.Bits(22, 10) != 0b_11_1110_0101)
            return null;

        var imm = inst.Bits(10, 12);
        var regT = inst.Bits(0, 5);
        var regN = inst.Bits(5, 5);

        return (regT, regN, imm);
    }

    public static bool IsB(uint inst) => inst.Bits(26, 6) == 0b_000101;

    public static Dictionary<uint, ulong> GetAddressesLoadedIntoRegisters(List<uint> funcBody, ulong baseAddress, ElfFile image)
    {
        var ret = new Dictionary<uint, ulong>();

        var pc = baseAddress;
        foreach (var inst in funcBody)
        {
            //ADRP Xn, #page instruction
            if (GetAdrp(inst, pc) is var (reg, page))
            {
                ret[reg] = page;
            }

            //ADR Xn, addr
            if (GetAdr(inst, pc) is var (adrReg, addr))
            {
                ret[adrReg] = addr;
            }

            //Add Xn, Xd, #imm
            if (GetAdd64(inst) is var (regN, regD, imm))
            {
                //Check adding to self (n == d) and we have the register
                if (regN == regD && ret.ContainsKey(regD))
                    ret[regD] += imm;
            }

            //LDR Xm, [Xn, #offset]
            if (GetLdr64ImmOffset(inst) is var (regT, regLdrN, simm))
            {
                //Check ldr is to self and we have the reg
                if (regT == regLdrN && ret.ContainsKey(regLdrN))
                {
                    ret[regLdrN] += simm * 8;

                    //Dereference resulting pointer
                    ret[regLdrN] = image.ReadPointerAtVirtualAddress(ret[regLdrN]);
                }
            }

            pc += 4;
        }

        return ret;
    }

    public static List<uint> ReadFunctionAtRawAddress(ElfFile file, uint loc, uint maxLength)
    {
        //Either we find a b (hard jump), or we exceed maxLength

        var ret = new List<uint>();

        uint inst;
        file.Position = file.MapVirtualAddressToRaw(loc);
        do
        {
            inst = file.ReadUInt32();
            ret.Add(inst);
        } while (!IsB(inst) && ret.Count < maxLength);

        return ret;
    }
}
