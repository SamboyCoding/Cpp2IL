namespace Cpp2IL.Core.ISIL;

public enum IsilMnemonic
{
    Move,
    LoadAddress,
    Call,
    CallNoReturn,
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
    Compare,
    ShiftStack,
    Return,
    Goto,
    JumpIfEqual,
    JumpIfNotEqual,
    JumpIfGreater,
    JumpIfGreaterOrEqual,
    JumpIfLess,
    JumpIfLessOrEqual,
    Interrupt,
    NotImplemented
}