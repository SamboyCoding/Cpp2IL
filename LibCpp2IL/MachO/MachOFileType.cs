namespace LibCpp2IL.MachO;

public enum MachOFileType : uint
{
    MH_OBJECT = 0x1,
    MH_EXECUTE = 0x2,
    MH_DYLIB = 0x6, //Dynamic library
    MH_BUNDLE = 0x8,
    MH_DSYM = 0xA,
    MH_KEXT_BUNDLE = 0xB, //Kernel Extension Bundle
    MH_APP_EXTENSION_SAFE = 0x02000000,
    MY_SIM_SUPPORT = 0x08000000,
    MH_DYLIB_IN_CACHE = 0x80000000,
}
