namespace LibCpp2IL.Elf;

public enum ElfDynamicSymbolType : byte
{
    STT_NOTYPE = 0,
    STT_OBJECT = 1,
    STT_FUNC = 2,
    STT_SECTION = 3,
    STT_FILE = 4,
    STT_COMMON = 5,
    STT_LOOS = 10,
    STT_HIOS = 12,
    STT_LOPROC = 13,
    STT_SPARC_REGISTER = 13,
    STT_HIPROC = 15,
}
