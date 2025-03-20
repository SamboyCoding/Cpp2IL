using System.Diagnostics;
using System.Text;
using Cpp2IL.Decompiler.IL;

namespace Cpp2IL.Decompiler.ControlFlow;

/// <summary>
/// A block in the control flow graph.
/// </summary>
[DebuggerDisplay("Id = {Id}, Instructions = {Instructions.Count}")]
public class Block
{
    /// <summary>
    /// ID of the block.
    /// </summary>
    public int Id = -1;

    /// <summary>
    /// Is the block dirty?
    /// </summary>
    public bool IsDirty = false;

    /// <summary>
    /// Instructions that make up the block.
    /// </summary>
    public List<Instruction> Instructions = [];

    /// <summary>
    /// Blocks that can flow into this block.
    /// </summary>
    public List<Block> Predecessors = [];

    /// <summary>
    /// Blocks that this block can flow into.
    /// </summary>
    public List<Block> Successors = [];

    /// <summary>
    /// Operands that the block uses.
    /// </summary>
    public List<object> Use = [];

    /// <summary>
    /// Operands that the block defines.
    /// </summary>
    public List<object> Def = [];

    /// <summary>
    /// True if the block doesn't affect control flow.
    /// </summary>
    public bool IsFallThrough => Instructions.Count != 0 && Instructions.Last().IsFallThrough;

    /// <summary>
    /// Is the last instruction a call?
    /// </summary>
    public bool IsCall => Instructions.Count != 0 && Instructions.Last().IsCall;

    /// <summary>
    /// Is the last instruction a tail call?
    /// </summary>
    public bool IsTailCall => Instructions.Count != 0 && Instructions.Last().IsTailCall;

    /// <summary>
    /// Adds an instruction to the block.
    /// </summary>
    /// <param name="instruction">The instruction.</param>
    public void AddInstruction(Instruction instruction) => Instructions.Add(instruction);

    public override string ToString() => $"Block {Id}\nUse: {string.Join(", ", Use)}\nDef: {string.Join(", ", Def)}\n{string.Join("\n", Instructions)}";
}
