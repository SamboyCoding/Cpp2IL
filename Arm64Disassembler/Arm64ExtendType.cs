namespace Arm64Disassembler;

public enum Arm64ExtendType : byte
{
    UXTB,
    UXTH,
    UXTW,
    UXTX,
    SXTB,
    SXTH,
    SXTW,
    SXTX,
    
    NONE,
}