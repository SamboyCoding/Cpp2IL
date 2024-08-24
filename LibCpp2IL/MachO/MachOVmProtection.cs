using System;

namespace LibCpp2IL.MachO;

[Flags]
public enum MachOVmProtection
{
    PROT_NONE = 0x0,
    PROT_READ = 0x1,
    PROT_WRITE = 0x2,
    PROT_EXEC = 0x4,
    PROT_NO_CHANGE = 0x8,
    PROT_COPY = 0x10,
    PROT_TRUSTED = 0x20,
    PROT_IS_MASK = 0x40,
    PROT_STRIP_READ = 0x80,
    PROT_COPY_FAIL_IF_EXECUTABLE = 0x100,
}
