namespace LibCpp2IL.Elf;

public enum ElfSectionEntryType : uint
{
    SHT_NONE = 0,
    SHT_PROGBITS = 1,
    SHT_SYMTAB = 2,
    SHT_STRTAB = 3,
    SHT_RELA = 4,
    SHT_HASH = 5,
    SHT_DYNAMIC = 6,
    SHT_NOTE = 7,
    SHT_NOBITS = 8,
    SHT_REL = 9,
    SHT_SHLIB = 0xA,
    SHT_DYNSYM = 0xB,
    SHT_INIT_ARRAY = 0xE,
    SHT_FINI_ARRAY = 0xF,
    SHT_PREINIT_ARRAY = 0x10,
    SHT_GROUP = 0x11,
    SHT_SYMTAB_SHNDX = 0x12,
    SHT_NUM = 0x13,
    SHT_LOOS = 0x60000000,
    SHT_HIOS = 0x6FFFFFFF,
    SHT_LOPROC = 0x70000000,
    SHT_HIPROC = 0x7FFFFFFF,
}
