namespace Decompiler.IL;

/// <summary>
/// All operand types.
/// </summary>
public enum OperandType
{
    /// <summary>
    /// A number (int).
    /// </summary>
    Int,

    /// <summary>
    /// A number (long).
    /// </summary>
    Long,

    /// <summary>
    /// A number (ulong).
    /// </summary>
    Ulong,

    /// <summary>
    /// A string.
    /// </summary>
    String,

    /// <summary>
    /// Branch target instruction (code).
    /// </summary>
    BranchTargetInstruction,

    /// <summary>
    /// Branch target block (code).
    /// </summary>
    BranchTargetBlock,

    /// <summary>
    /// A register.
    /// </summary>
    Register,

    /// <summary>
    /// Offset on the stack.
    /// </summary>
    StackOffset,

    /// <summary>
    /// Call info (method and params).
    /// </summary>
    CallInfo,

    /// <summary>
    /// Nested instruction.
    /// </summary>
    Instruction
}
