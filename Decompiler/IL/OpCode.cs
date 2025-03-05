namespace Decompiler.IL;

/// <summary>
/// All opcodes.
/// </summary>
public enum OpCode
{
    Unknown,
    Nop,

    Move,
    Write,
    Read,

    Call,
    Return,

    Jump,
    ConditionalJump,

    ShiftStack,

    Add,
    Subtract,
    Multiply,
    Divide,
    ShiftLeft,
    ShiftRight,
    And,
    Or,
    Xor,
    Not,
    Negate,

    CheckEqual,
    CheckGreater,
    CheckLess
}
