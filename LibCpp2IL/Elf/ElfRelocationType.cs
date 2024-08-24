namespace LibCpp2IL.Elf;

public enum ElfRelocationType : uint
{
    R_ARM_ABS32 = 2,
    R_ARM_REL32 = 3,
    R_ARM_PC13 = 4,
    R_ARM_COPY = 20,

    R_AARCH64_ABS64 = 0x101,
    R_AARCH64_PREL64 = 0x104,
    R_AARCH64_GLOB_DAT = 0x401,
    R_AARCH64_JUMP_SLOT = 0x402,
    R_AARCH64_RELATIVE = 0x403,

    R_386_32 = 1,
    R_386_PC32 = 2,
    R_386_GLOB_DAT = 6,
    R_386_JMP_SLOT = 7,

    R_AMD64_64 = 1,
    R_AMD64_RELATIVE = 8,
}
