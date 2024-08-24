using System;

namespace LibCpp2IL.MachO;

[Flags]
public enum MachOSegmentFlags
{
    SG_NONE = 0x0,

    SG_HIGHVM = 0x1, // The file contents for this segment are for the high part of the virtual space - push the raw data as far to the end of the segment as possible.
    SG_FVMLIB = 0x2, // This segment is the VM that is allocated by a fixed VM library, for overlap checking.
    SG_NORELOC = 0x4, // This segment has nothing that was relocated in it and nothing relocated to it, so no relocations are needed.
    SG_PROTECTED_VERSION_1 = 0x8, // This segment is protected.  If the segment starts at file offset 0, the first page of the segment is not protected.  All other pages of the segment are protected.
}
