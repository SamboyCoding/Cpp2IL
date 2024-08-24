using System;

namespace LibCpp2IL.Elf;

[Flags]
public enum ElfSectionHeaderFlags : long
{
    SHF_WRITE = 0x1,
    SHF_ALLOC = 0x2,
    SHF_EXECINSTR = 0x4,
    SHF_MERGE = 0x10,
    SHF_STRINGS = 0x20,
    SHF_INFO_LINK = 0x40,
    SHF_LINK_ORDER = 0x80,
    SHF_OS_NONCONFORMING = 0x100,
    SHF_GROUP = 0x200,
    SHF_TLS = 0x400,

    SHF_MASKOS = 0x0FF0_0000,
    SHF_MASKPROC = 0xF000_0000,
    SHF_ORDERED = 0x4000_0000,
    SHF_EXCLUDE = 0x8000_0000,
}
