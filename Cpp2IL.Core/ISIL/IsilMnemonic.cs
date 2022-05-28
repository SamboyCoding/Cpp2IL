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
    Push,
    Pop,
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