using Arm64Disassembler.InternalDisassembly;

namespace Arm64Disassembler;

public static class Arm64EnumExtensions
{
    public static bool IsSp(this Arm64Register register) => register is Arm64Register.X31 or Arm64Register.W31;
    public static bool IsZr(this Arm64Register register) => register.IsSp();

    public static Arm64ConditionCode Invert(this Arm64ConditionCode conditionCode) => conditionCode switch
    {
        Arm64ConditionCode.EQ => Arm64ConditionCode.NE,
        Arm64ConditionCode.NE => Arm64ConditionCode.EQ,
        Arm64ConditionCode.CS => Arm64ConditionCode.CC,
        Arm64ConditionCode.CC => Arm64ConditionCode.CS,
        Arm64ConditionCode.MI => Arm64ConditionCode.PL,
        Arm64ConditionCode.PL => Arm64ConditionCode.MI,
        Arm64ConditionCode.VS => Arm64ConditionCode.VC,
        Arm64ConditionCode.VC => Arm64ConditionCode.VS,
        Arm64ConditionCode.HI => Arm64ConditionCode.LS,
        Arm64ConditionCode.LS => Arm64ConditionCode.HI,
        Arm64ConditionCode.GE => Arm64ConditionCode.LT,
        Arm64ConditionCode.LT => Arm64ConditionCode.GE,
        Arm64ConditionCode.GT => Arm64ConditionCode.LE,
        Arm64ConditionCode.LE => Arm64ConditionCode.GT,
        _ => conditionCode
    };
}