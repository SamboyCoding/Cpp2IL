using Cpp2IL.Core.Analysis;

namespace Cpp2IL.Core.ISIL;

/// <summary>
/// If changing this, also update <see cref="Instruction"/>
/// </summary>
public enum OpCode
{
    /// <summary>Invalid instruction, op 1 is the debug string</summary>
    Invalid,

    /// <summary>Not implemented instruction, op 1 is the debug string</summary>
    NotImplemented,

    /// <summary>
    /// Interrupt, kept for stack analysis
    /// </summary>
    Interrupt,

    /// <summary>
    /// No operation
    /// </summary>
    Nop,

    /// <summary>Moves op 2 into op 1</summary>
    Move,

    /// <summary>Moves the result of phi function into op 1, other operands are inputs</summary>
    Phi,

    /// <summary>Calls a method @ op 1, moves the result into op 2, and the rest are params</summary>
    Call,

    /// <summary>Calls a method @ op 1, the rest are params</summary>
    CallVoid,

    /// <summary>Calls a method @ op 1, the rest are params</summary>
    IndirectCall,

    /// <summary>Returns from the method, op 1 is the value to return (optional)</summary>
    Return,

    /// <summary>Jumps to op 1</summary>
    Jump,

    /// <summary>Jumps to op 1</summary>
    IndirectJump,

    /// <summary><c>If op 2 is true, jumps to op 1</summary>
    ConditionalJump,

    /// <summary>Adds op 1 to stack pointer</summary>
    ShiftStack,

    /// <summary>Adds op 2 and op 3, and moves the result into op 1</summary>
    Add,

    /// <summary>Subtracts op 3 from op 2, and moves the result into op 1</summary>
    Subtract,

    /// <summary>Multiplies op 2 by op 3, and moves the result into op 1</summary>
    Multiply,

    /// <summary>Divides op 2 by op 3, and moves the result into op 1</summary>
    Divide,

    /// <summary>Divides op 2 by op 3, and moves the remainder into op 1</summary>
    Modulo,

    /// <summary>Shifts the bits of op 2 left by op 3, and moves the result into op 1</summary>
    ShiftLeft,

    /// <summary>Shifts the bits of op 2 right by op 3, and moves the result into op 1</summary>
    ShiftRight,

    /// <summary>Bitwise AND on op 2 and op 3, moves the result into op 1</summary>
    And,

    /// <summary>Bitwise OR on op 2 and op 3, moves the result into op 1</summary>
    Or,

    /// <summary>Bitwise XOR on op 2 and op 3, moves the result into op 1</summary>
    Xor,

    /// <summary>Logical not on op 2, moves the result into op 1</summary>
    Not,

    /// <summary>Negates op 2, moves the result into op 1</summary>
    Negate,

    /// <summary>Moves 1 into op 1, if op 2 and op 3 are equal</summary>
    CheckEqual,

    /// <summary>Moves 1 into op 1, if op 2 is greater than op 3</summary>
    CheckGreater,

    /// <summary>Moves 1 into op 1, if op 2 is less than op 3</summary>
    CheckLess,

    /// <summary>Moves 1 into op 1, if op 2 and op 3 are not equal</summary>
    CheckNotEqual,

    /// <summary>Moves 1 into op 1, if op 2 is greater than or equal to op 3</summary>
    CheckGreaterOrEqual,

    /// <summary>Moves 1 into op 1, if op 2 is less than or equal to op 3</summary>
    CheckLessOrEqual,

    /// <summary>
    /// Allocates a new, uninitialized instance of the type described by op 2 and moves it into op 1.
    /// </summary>
    Newobj,

    /// <summary>
    /// Allocates a new array of the type described by op 2, with the length in op 3, into op 1.
    /// </summary>
    NewArr,

    /// <summary>
    /// Boxes the value at op 3 as the type described by op 2, and moves the result into op 1.
    /// </summary>
    Box,

    /// <summary>
    /// Throws a new instance of the exception type described by op 1.
    /// </summary>
    Throw
}
