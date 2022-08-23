namespace Arm64Disassembler;

//Ref C1-225
public enum Arm64ConditionCode : byte
{
    EQ, // Equal
    NE, // Not equal
    CS, // Carry set (greater than or equal)
    CC, // Carry clear (less than)
    MI, // Minus (less than)
    PL, // Plus (greater than or equal)
    VS, // Overflow (unordered)
    VC, // No overflow (ordered)
    HI, // Unsigned higher (greater than)
    LS, // Unsigned lower or same (less than or equal)
    GE, // Signed greater than or equal (greater than or equal)
    LT, // Signed less than (less than)
    GT, // Signed greater than (greater than)
    LE, // Signed less than or equal (less than or equal)
    AL, // Always (unconditional)
    NV, // Identical to always (unconditional), exists only to provide a valid decoding for 0b1111.
    
    NONE, // Meta-value - indicates no condition code
}