using System;

namespace LibCpp2IL.MachO;

[Flags]
public enum MachOSectionFlags : uint
{
    TYPE_BITMASK = 0x000000FF,
    ATTRIBUTES_BITMASK = 0xFFFFF00,
    USER_ATTRIBUTES_BITMASK = 0xFF000000, //User-set attributes
    SYSTEM_ATTRIBUTES_BITMASK = 0x00FFFF00, //System-set attributes

    TYPE_REGULAR = 0x0,
    TYPE_ZEROFILL = 0x1, //Zero-fill on demand
    TYPE_CSTRING_LITERALS = 0x2, //Only literal C strings
    TYPE_4BYTE_LITERALS = 0x3, //Only 4-byte literals
    TYPE_8BYTE_LITERALS = 0x4, //Only 8-byte literals
    TYPE_LITERAL_POINTERS = 0x5, //Only pointers to literals
    TYPE_NON_LAZY_SYMBOL_POINTERS = 0x6, //Only non-lazy symbol pointers
    TYPE_LAZY_SYMBOL_POINTERS = 0x7, //Only lazy symbol pointers
    TYPE_SYMBOL_STUBS = 0x8, //Only symbol stubs
    TYPE_MOD_INIT_FUNC_POINTERS = 0x9, //Only function pointers for initialization
    TYPE_MOD_TERM_FUNC_POINTERS = 0xA, //Only function pointers for termination
    TYPE_COALESCED = 0xB, //Contains symbols that are to be coalesced
    TYPE_GB_ZEROFILL = 0xC, //Zero-fill on demand, can be > 4gb
    TYPE_INTERPOSING = 0xD, //Only pairs of function pointers for interposing
    TYPE_16BYTE_LITERALS = 0xE, //Only 16-byte literals
    TYPE_DTRACE_DOF = 0xF, //Contains DTrace Object Format
    TYPE_LAZY_DYLIB_SYMBOL_POINTERS = 0x10, //Only lazy symbol pointers to lazy dylibs
    TYPE_THREAD_LOCAL_REGULAR = 0x11, // Template of initial values for thread local variables
    TYPE_THREAD_LOCAL_ZEROFILL = 0x12, // Zero-fill on demand template for thread local variables
    TYPE_THREAD_LOCAL_VARIABLES = 0x13, // Thread local variable descriptors
    TYPE_THREAD_LOCAL_VARIABLE_POINTERS = 0x14, // Pointers to thread local variable descriptors
    TYPE_THREAD_LOCAL_INIT_FUNCTION_POINTERS = 0x15, // Pointers functions to call to initialize TLV values
    TYPE_INIT_FUNC_OFFSETS = 0x16, // 32-bit offsets to initialization functions

    ATTR_PURE_INSTRUCTIONS = 0x80000000, // Section contains only true machine instructions
    ATTR_NO_TOC = 0x40000000, // Section contains coalesced symbols that are not to be in a ranlib table of contents
    ATTR_STRIP_STATIC_SYMS = 0x20000000, // Ok to strip static symbols in this section in files with the MH_DYLDLINK flag
    ATTR_NO_DEAD_STRIP = 0x10000000, // No dead stripping
    ATTR_LIVE_SUPPORT = 0x08000000, // Blocks are live if they reference live blocks
    ATTR_SELF_MODIFYING_CODE = 0x04000000, // Used with i386 code stubs written on by dyld
    ATTR_DEBUG = 0x02000000, // A debug section

    ATTR_SOME_INSTRUCTIONS = 0x00000400, // Section contains some machine instructions
    ATTR_EXT_RELOC = 0x00000200, // Section has external relocation entries
    ATTR_LOC_RELOC = 0x00000100, // Section has local relocation entries
}
