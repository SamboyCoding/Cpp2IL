namespace Cpp2IL.Core.ISIL;

/// <summary>
/// If changing this, also update <see cref="Instruction"/>
/// </summary>
public enum OpCode
{
    /// <summary>Logs to console (in the decompiled code) that there was an invalid instruction, first operand is the instruction string</summary>
    Invalid,

    /// <summary>Logs to console (in the decompiled code) that there was a not implemented instruction, first operand is the instruction string</summary>
    NotImplemented,

    /// <summary>
    /// Interrupt, kept for stack analysis
    /// </summary>
    Interrupt,

    /// <summary>
    /// No operation
    /// </summary>
    Nop,

    /// <summary>Moves the second operand into the first</summary>
    Move,

    /// <summary>Moves the result of phi function into first operand, other operands are inputs</summary>
    Phi,

    /// <summary>Calls a method (first operand), moves the result into second, and the rest are params</summary>
    Call,

    /// <summary>Calls a method (first operand), rest are params</summary>
    CallVoid,

    /// <summary>Calls a method (first operand), rest are params</summary>
    IndirectCall,

    /// <summary>Returns from the method, the return operand is optional</summary>
    Return,

    /// <summary>Jumps to the first operand</summary>
    Jump,

    /// <summary>Jumps to the first operand</summary>
    IndirectJump,

    /// <summary><c>If the second operand is true, jumps to the first</summary>
    ConditionalJump,

    /// <summary>Adds the first operand to stack pointer</summary>
    ShiftStack,

    /// <summary>Adds the second and third operands and moves the result into the first</summary>
    Add,

    /// <summary>Subtracts the third operand from the second and moves the result into the first</summary>
    Subtract,

    /// <summary>Multiplies the second operand by the third and moves the result into the first</summary>
    Multiply,

    /// <summary>Divides the second operand by the third and moves the result into the first</summary>
    Divide,

    /// <summary>Shifts the bits of the second operand left by the third and moves the result into the first</summary>
    ShiftLeft,

    /// <summary>Shifts the bits of the second operand right by the third and moves the result into the first</summary>
    ShiftRight,

    /// <summary>Performs and on the second and third operands and moves the result into the first</summary>
    And,

    /// <summary>Performs or on the second and third operands and moves the result into the first</summary>
    Or,

    /// <summary>Performs xor on the second and third operands and moves the result into the first</summary>
    Xor,

    /// <summary>Performs not on the second operands and moves the result into the first</summary>
    Not,

    /// <summary>Moves the negated second operand into the first</summary>
    Negate,

    /// <summary>Moves 1 into the first operand, if the second and third are equal</summary>
    CheckEqual,

    /// <summary>Moves 1 into the first operand, if the second is greater than the third</summary>
    CheckGreater,

    /// <summary>Moves 1 into the first operand, if the second is less than the third</summary>
    CheckLess
}
