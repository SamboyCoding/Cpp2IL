using Decompiler.ControlFlow;

namespace Decompiler.IL;

/// <summary>
/// Branch target block operand.
/// </summary>
public class BranchTargetBlock(Block block) : IOperand
{
    public OperandType Type => OperandType.BranchTargetBlock;

    /// <summary>
    /// The target block.
    /// </summary>
    public Block Block = block;

    public override string ToString() => Block.Instructions.Count == 0 ? $"@b{Block.Id}" : $"@b{Block.Id}:{Block.Instructions[0]}";
}
