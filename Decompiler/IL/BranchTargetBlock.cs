using Decompiler.ControlFlow;

namespace Decompiler.IL;

/// <summary>
/// Branch target block operand.
/// </summary>
public struct BranchTargetBlock(Block block) : IOperand
{
    public OperandType Type => OperandType.BranchTargetBlock;

    /// <summary>
    /// The target block.
    /// </summary>
    public Block Block = block;

    public override string ToString() => $"@b{Block.Id}";
}
