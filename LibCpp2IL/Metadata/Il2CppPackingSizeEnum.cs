using System;

namespace LibCpp2IL.Metadata;

public enum Il2CppPackingSizeEnum : uint
{
    Zero,
    One,
    Two,
    Four,
    Eight,
    Sixteen,
    ThirtyTwo,
    SixtyFour,
    OneHundredTwentyEight
}

public static class Il2CppPackingSizeEnumExtensions
{
    public static uint NumericalValue(this Il2CppPackingSizeEnum size) => size switch
    {
        Il2CppPackingSizeEnum.Zero => 0,
        Il2CppPackingSizeEnum.One => 1,
        Il2CppPackingSizeEnum.Two => 2,
        Il2CppPackingSizeEnum.Four => 4,
        Il2CppPackingSizeEnum.Eight => 8,
        Il2CppPackingSizeEnum.Sixteen => 16,
        Il2CppPackingSizeEnum.ThirtyTwo => 32,
        Il2CppPackingSizeEnum.SixtyFour => 64,
        Il2CppPackingSizeEnum.OneHundredTwentyEight => 128,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
    };
}
