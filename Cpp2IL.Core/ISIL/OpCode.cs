namespace Cpp2IL.Core.ISIL;

/// <summary>
/// If changing this, also update <see cref="Instruction"/>
/// </summary>
public enum OpCode // There is some weird stuff in doc comments because i can't use <, >, & (idk why & doesn't work)
                   // doc comments are in this format: Move dest, src : dest = src
                   //                                  [isil]           [decompiled code/what the instruction does]
{
    /// <summary>Invalid (optional) text</summary>
    Invalid,

    /// <summary>NotImplemented (optional) text</summary>
    NotImplemented,

    /// <summary>Interrupt</summary>
    Interrupt,

    /// <summary>No operation</summary>
    Nop,

    /// <summary>Move dest, src : <c>dest = src</c></summary>
    Move,

    /// <summary>Phi dest, src1, src2, etc. : <c>dest = phi(src1, src2, etc.)</c></summary>
    Phi,

    /// <summary>Call target, dest, arg1, arg2, etc. : <c>dest = target(arg1, arg2, etc.)</c></summary>
    Call,

    /// <summary>CallVoid target, arg1, arg2, etc. : <c>target(arg1, arg2, etc.)</c></summary>
    CallVoid,

    /// <summary>IndirectCallVoid target, arg1, arg2, etc. : <c>target(arg1, arg2, etc.)</c></summary>
    IndirectCall,

    /// <summary>Return (optional) value : <c>return value</c></summary>
    Return,

    /// <summary>Jump target : <c>goto target</c></summary>
    Jump,

    /// <summary>IndirectJump target : <c>goto target</c></summary>
    IndirectJump,

    /// <summary>ConditionalJump target, cond : <c>if (cond) goto target</c></summary>
    ConditionalJump,

    /// <summary>ShiftStack value : <c>sp += value</c></summary>
    ShiftStack,

    /// <summary>Add dest, l, r : <c>dest = l + r</c></summary>
    Add,

    /// <summary>Subtract dest, l, r : <c>dest = l - r</c></summary>
    Subtract,

    /// <summary>Multiply dest, l, r : <c>dest = l * r</c></summary>
    Multiply,

    /// <summary>Divide dest, l, r : <c>dest = l / r</c></summary>
    Divide,

    /// <summary>ShiftLeft dest, src, count : <c>dest = src shl count</c></summary>
    ShiftLeft,

    /// <summary>ShiftRight dest, src, count : <c>dest = src shr count</c></summary>
    ShiftRight,

    /// <summary>And dest, l, r : <c>dest = l and r</c></summary>
    And,

    /// <summary>Or dest, l, r : <c>dest = l | r</c></summary>
    Or,

    /// <summary>Xor dest, l, r : <c>dest = l ^ r</c></summary>
    Xor,

    /// <summary>Not dest, src : <c>dest = !src</c></summary>
    Not,

    /// <summary>Negate dest, src : <c>dest = -src</c></summary>
    Negate,

    /// <summary>CheckEqual dest, l, r : <c>dest = l == r</c></summary>
    CheckEqual,

    /// <summary>CheckGreater dest, l, r : <c>dest = l greater r</c></summary>
    CheckGreater,

    /// <summary>CheckLess dest, l, r : <c>dest = l less r</c></summary>
    CheckLess
}
