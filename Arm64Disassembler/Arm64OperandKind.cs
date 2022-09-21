namespace Arm64Disassembler;

public enum Arm64OperandKind
{
    /// <summary>
    /// There is no operand in this slot.
    /// </summary>
    None,
    /// <summary>
    /// The operand in this slot is a register.
    /// </summary>
    Register,
    /// <summary>
    /// The operand in this slot is a raw immediate value.
    /// </summary>
    Immediate,
    /// <summary>
    /// The operand in this slot is an immediate value but it is intended to be added to the PC (<see cref="Arm64Instruction.Address"/>). Bear in mind the immediate can be negative
    /// </summary>
    ImmediatePcRelative,
    /// <summary>
    /// The operand in this slot is a memory operand. Use the <see cref="Arm64Instruction.MemBase"/>, <see cref="Arm64Instruction.MemOffset"/>, and <see cref="Arm64Instruction.MemIsPreIndexed"/> properties to access the operand.
    /// </summary>
    Memory
}
