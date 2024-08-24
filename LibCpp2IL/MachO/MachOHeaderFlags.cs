using System;

namespace LibCpp2IL.MachO;

[Flags]
public enum MachOHeaderFlags
{
    MH_NO_UNDEFINED_SYMBOLS = 0x1,
    MH_INCREMENTAL_LINK = 0x2,
    MH_DYLDLINK = 0x4, // this file is input for the dynamic linker
    MH_BINDATLOAD = 0x8, // this file's undefined symbols are bound at load time
    MH_PREBOUND = 0x10, // this file has its undefined symbols prebound
    MH_SPLIT_SEGS = 0x20, // this file has its read-only and read-write segments split
    MH_LAZY_INIT = 0x40, // the shared library init routine should be run lazily. Obsolete.
    MH_TWOLEVEL = 0x80, // this image is using two-level name space bindings
    MH_FORCE_FLAT = 0x100, // this executable is forcing all images to use flat name space bindings
    MH_NOMULTIDEFS = 0x200, // guarantee of no multiple definitions of symbols in sub-images, so two-level namespace bindings can work
    MH_NOFIXPREBINDING = 0x400, // do not have dyld notify the prebinding agent about this executable
    MH_PREBINDABLE = 0x800, // the binary is not prebound but can have its prebinding redone. only used when MH_PREBOUND is not set.
    MH_ALLMODSBOUND = 0x1000, // this binary binds to all two-level namespace modules of its dependent libraries. only used when MH_PREBINDABLE and MH_TWOLEVEL are both set.
    MH_SUBSECTIONS_VIA_SYMBOLS = 0x2000, // safe to divide up the sections into sub-sections via symbols for dead code stripping
    MH_CANONICAL = 0x4000, // the binary has been canonicalized via the unprebind operation
    MH_WEAK_DEFINES = 0x8000, // the final linked image contains external weak symbols
    MH_BINDS_TO_WEAK = 0x10000, // the final linked image uses weak symbols
    MH_ALLOW_STACK_EXECUTION = 0x20000, // When this bit is set, all stacks in the task will be given stack execution privilege.  Only used in MH_EXECUTE filetypes.
    MH_ROOT_SAFE = 0x40000, // When this bit is set, the binary declares it is safe for use in processes with uid zero
    MH_SETUID_SAFE = 0x80000, // When this bit is set, the binary declares it is safe for use in processes when issetugid() is true
    MH_NO_REEXPORTED_DYLIBS = 0x100000, // When this bit is set on a dylib, the static linker does not need to examine dependent dylibs to see if any are re-exported
    MH_PIE = 0x200000, // When this bit is set, the OS will load the main executable at a random address.  Only used in MH_EXECUTE filetypes.
    MH_DEAD_STRIPPABLE_DYLIB = 0x400000, // Only for use on dylibs.  Allows the linker to not link to this dylib if it's not referenced.
    MH_HAS_TLV_DESCRIPTORS = 0x800000, // Contains a section of type S_THREAD_LOCAL_VARIABLES..
    MH_NO_HEAP_EXECUTION = 0x1000000, // When this bit is set, the OS will run the main executable with a non-executable heap even on platforms (e.g. i386) that don't require it. Only used in MH_EXECUTE filetypes.
    MH_APP_EXTENSION_SAFE = 0x02000000, // The code was linked for use in an application extension.
    MH_NLIST_OUTOFSYNC_WITH_DYLDINFO = 0x04000000, // The external symbols listed in the nlist symtab do not include all the symbols that are used by the dynamic linker.
    MH_SIM_SUPPORT = 0x08000000, // The binary is suitable for a simulator.
}
