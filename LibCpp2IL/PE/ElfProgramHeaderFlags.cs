using System;

namespace LibCpp2IL.PE;

[Flags]
public enum ElfProgramHeaderFlags : uint
{
    PF_X = 1,
    PF_W = 2,
    PF_R = 4,
}
