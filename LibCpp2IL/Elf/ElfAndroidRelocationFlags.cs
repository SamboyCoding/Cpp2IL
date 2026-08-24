using System;

namespace LibCpp2IL.Elf;

// ReSharper disable InconsistentNaming
[Flags]
public enum ElfAndroidRelocationFlags : long
{
    RELOCATION_GROUPED_BY_INFO_FLAG = 1,
    RELOCATION_GROUPED_BY_OFFSET_DELTA_FLAG = 2,
    RELOCATION_GROUPED_BY_ADDEND_FLAG = 4,
    RELOCATION_GROUP_HAS_ADDEND_FLAG = 8,
}
