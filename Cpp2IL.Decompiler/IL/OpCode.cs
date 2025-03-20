namespace Cpp2IL.Decompiler.IL;

/// <summary>
/// All opcodes. If changing this, also update <see cref="Instruction"/>.
/// </summary>
public enum OpCode // There is some weird stuff in doc comments because i can't use <, >, & (idk why & doesn't work)
{
    /// <summary>Unknown (optional) text : <c>throw new Exception(text)</c></summary>
    Unknown,

    /// <summary>Nop</summary>
    Nop,

    /// <summary>Move dest, src : <c>dest = src</c></summary>
    Move,

    /// <summary>LoadAddress dest, src : <c>dest = address(src)</c></summary>
    LoadAddress,

    /// <summary>Phi dest, arg1, arg2, etc. : <c>dest = phi(arg1, arg2, etc.)</c></summary>
    Phi,

    /// <summary>Call dest, target, arg1, arg2, etc. : <c>dest = target(arg1, arg2, etc</c>.)</summary>
    Call,

    /// <summary>CallNoReturn target, arg1, arg2, etc. : <c>target(arg1, arg2, etc</c>.)</summary>
    CallVoid,

    /// <summary>TailCall dest, target, arg1, arg2, etc. : <c>dest = target(arg1, arg2, etc</c>.)</summary>
    TailCall,

    /// <summary>TailCallNoReturn target, arg1, arg2, etc. : <c>target(arg1, arg2, etc</c>.)</summary>
    TailCallVoid,

    /// <summary>Return value : <c>return value</c></summary>
    Return,

    /// <summary>Return : <c>return</c></summary>
    ReturnVoid,

    /// <summary>Jump target : <c>goto target</c></summary>
    Jump,

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
