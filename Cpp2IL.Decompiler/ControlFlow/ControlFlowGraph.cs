using Cpp2IL.Decompiler.IL;

namespace Cpp2IL.Decompiler.ControlFlow;

/// <summary>
/// A control flow graph.
/// Taken from https://github.com/SamboyCoding/Cpp2IL/blob/development/Cpp2IL.Core/Graphs/ISILControlFlowGraph.cs
/// </summary>
public class ControlFlowGraph
{
    /// <summary>
    /// All blocks of the graph.
    /// </summary>
    public List<Block> Blocks;

    /// <summary>
    /// The entry block.
    /// </summary>
    public Block EntryBlock;

    /// <summary>
    /// The exit block.
    /// </summary>
    public Block ExitBlock;

    /// <summary>
    /// All instructions.
    /// </summary>
    public List<Instruction> AllInstructions
    {
        get
        {
            var instructions = new List<Instruction>();
            foreach (var block in Blocks)
                instructions.AddRange(block.Instructions);
            return instructions;
        }
    }

    private int _nextId;

    private ControlFlowGraph()
    {
        EntryBlock = new Block() { Id = _nextId++ };
        ExitBlock = new Block() { Id = _nextId++ };
        Blocks = [EntryBlock, ExitBlock];
    }

    /// <summary>
    /// Builds a control flow graph from instructions.
    /// </summary>
    /// <param name="instructions">All instructions.</param>
    public static ControlFlowGraph Build(List<Instruction> instructions)
    {
        var graph = new ControlFlowGraph();

        var currentBlock = new Block() { Id = graph._nextId++ };

        graph.Blocks.Add(currentBlock);
        AddDirectedEdge(graph.EntryBlock, currentBlock);

        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            var isLast = i == instructions.Count - 1;

            switch (instruction.OpCode)
            {
                case OpCode.Jump:
                case OpCode.ConditionalJump:
                    currentBlock.AddInstruction(instruction);

                    if (!isLast)
                    {
                        var newBlock = new Block() { Id = graph._nextId++ };
                        graph.Blocks.Add(newBlock);

                        if (instruction.OpCode == OpCode.ConditionalJump)
                            AddDirectedEdge(currentBlock, newBlock);

                        currentBlock.IsDirty = true;
                        currentBlock = newBlock;
                    }
                    else
                    {
                        AddDirectedEdge(currentBlock, graph.ExitBlock);

                        if (instruction.OpCode == OpCode.Jump)
                            currentBlock.IsDirty = true;
                    }

                    break;

                case OpCode.Return:
                case OpCode.ReturnVoid:
                    currentBlock.AddInstruction(instruction);

                    if (!isLast)
                    {
                        var newBlock = new Block() { Id = graph._nextId++ };
                        graph.Blocks.Add(newBlock);
                        AddDirectedEdge(currentBlock, graph.ExitBlock);
                        currentBlock = newBlock;
                    }
                    else
                    {
                        AddDirectedEdge(currentBlock, graph.ExitBlock);
                    }

                    break;

                case OpCode.Call:
                case OpCode.CallVoid:
                case OpCode.TailCall:
                case OpCode.TailCallVoid:
                case OpCode.Unknown:
                    currentBlock.AddInstruction(instruction);

                    if (!isLast)
                    {
                        var newBlock = new Block() { Id = graph._nextId++ };
                        graph.Blocks.Add(newBlock);
                        AddDirectedEdge(currentBlock, newBlock);
                        currentBlock = newBlock;
                    }
                    else
                    {
                        AddDirectedEdge(currentBlock, graph.ExitBlock);
                    }

                    break;

                default:
                    currentBlock.AddInstruction(instruction);
                    break;
            }
        }

        for (var i = 0; i < graph.Blocks.Count; i++)
        {
            var block = graph.Blocks[i];

            if (block.IsDirty)
                graph.SplitTargetBlock(block);
        }

        // Connect blocks without successors to exit
        foreach (var block in graph.Blocks)
        {
            if (block.Successors.Count == 0 && block != graph.EntryBlock && block != graph.ExitBlock)
                AddDirectedEdge(block, graph.ExitBlock);
        }

        // Change branch targets to blocks
        foreach (var instruction in graph.Blocks.SelectMany(block => block.Instructions))
        {
            if (instruction.Operands.Count > 0 && instruction.Operands[0] is Instruction target)
                instruction.Operands[0] = graph.GetBlockByInstruction(target)!;
        }

        return graph;
    }

    /// <summary>
    /// Initially blocks are split by calls, this merges those blocks.
    /// </summary>
    public void MergeCallBlocks()
    {
        for (var i = 0; i < Blocks.Count - 1; i++)
        {
            var block = Blocks[i];
            if (!block.IsCall) continue;
            if (block.Successors.Count == 0) continue;
            var nextBlock = block.Successors[0];

            // Make sure that the next block only has one predecessor (this)
            if (nextBlock.Predecessors.Count != 1 || nextBlock.Predecessors[0] != block) continue;

            // Merge blocks
            block.Instructions.AddRange(nextBlock.Instructions);
            block.Successors = nextBlock.Successors;

            // Update the predecessors of the new successors
            foreach (var successor in nextBlock.Successors)
            {
                for (var j = 0; j < successor.Predecessors.Count; j++)
                {
                    if (successor.Predecessors[j] == nextBlock)
                        successor.Predecessors[j] = block;
                }
            }

            // Remove the merged block
            Blocks.Remove(nextBlock);
            i--;
        }
    }

    /// <summary>
    /// Checks if a local is used anywhere in the graph after an instruction.
    /// </summary>
    /// <param name="block">Block containing the instruction.</param>
    /// <param name="startIndex">Index of the instruction in the block.</param>
    /// <param name="local">The local.</param>
    /// <param name="usedByMemory">True if a memory operand uses the local.</param>
    /// <returns>True if the local is used.</returns>
    public static bool IsLocalUsedAfterInstruction(Block block, int startIndex, LocalVariable local, out bool usedByMemory)
    {
        usedByMemory = false;

        var visited = new HashSet<(Block, int)>();

        bool ProcessBlock(Block currentBlock, int index, out bool usedByMemory2)
        {
            usedByMemory2 = false;

            var key = (currentBlock, index);

            if (!visited.Add(key))
                return false;

            // Process instructions
            for (var i = index; i < currentBlock.Instructions.Count; i++)
            {
                var instruction = currentBlock.Instructions[i];

                // Direct usage check
                if (instruction.Sources.Contains(local))
                    return true;

                // Used in memory operand
                foreach (var source in instruction.Sources)
                {
                    if (source is MemoryAddress memory && (memory.Base == local || memory.Index == local))
                    {
                        usedByMemory2 = true;
                        return true;
                    }
                }
            }

            // Process successors
            foreach (var successor in currentBlock.Successors)
            {
                if (ProcessBlock(successor, 0, out usedByMemory2))
                    return true;
            }

            return false;
        }

        return ProcessBlock(block, startIndex, out usedByMemory);
    }

    /// <summary>
    /// Removes all nop instructions.
    /// </summary>
    public void RemoveNops()
    {
        foreach (var block in Blocks)
        {
            for (var i = 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];

                if (instruction.OpCode == OpCode.Nop)
                {
                    block.Instructions.RemoveAt(i);
                    i--;
                }
            }
        }
    }

    /// <summary>
    /// Removes all blocks that don't have any instructions.
    /// </summary>
    public void RemoveEmptyBlocks()
    {
        var emptyBlocks = Blocks.Where(b => b.Instructions.Count == 0).ToList();

        foreach (var block in emptyBlocks)
        {
            // Don't remove entry or exit
            if (block == EntryBlock || block == ExitBlock)
                continue;

            // Update successors
            foreach (var pred in block.Predecessors)
            {
                pred.Successors.Remove(block);
                pred.Successors.AddRange(block.Successors);
            }

            // Update predecessors
            foreach (var succ in block.Successors)
            {
                succ.Predecessors.Remove(block);
                succ.Predecessors.AddRange(block.Predecessors);
            }

            Blocks.Remove(block);
        }

        // Remove duplicates from successors and predecessors
        foreach (var block in Blocks)
        {
            block.Successors = block.Successors.Distinct().ToList();
            block.Predecessors = block.Predecessors.Distinct().ToList();
        }
    }

    /// <summary>
    /// Removes branches that point to removed blocks.
    /// </summary>
    public void FixBranches()
    {
        foreach (var block in Blocks)
        {
            if (block.Instructions.Count == 0)
                continue;

            var instruction = block.Instructions.Last();

            if (instruction.OpCode is OpCode.Jump or OpCode.ConditionalJump)
            {
                if (!Blocks.Contains(instruction.Operands[0]))
                    block.Instructions.RemoveAt(block.Instructions.Count - 1);
            }
        }
    }

    private void SplitTargetBlock(Block block)
    {
        if (block.IsFallThrough)
            return;

        // Get the branch target block
        var branch = block.Instructions.Last();
        var target = (Instruction)branch.Operands[0];
        var targetBlock = GetBlockByInstruction(target);

        // Split it at the target instruction
        var index = targetBlock!.Instructions.FindIndex(i => i == target);
        var targetBlock2 = SplitAndCreate(targetBlock, index);
        AddDirectedEdge(block, targetBlock2);

        block.IsDirty = false;
    }

    /// <summary>
    /// Gets the block that contains the instruction.
    /// </summary>
    /// <param name="instruction">The instruction.</param>
    /// <returns>The containing block.</returns>
    public Block? GetBlockByInstruction(Instruction instruction)
    {
        foreach (var block in Blocks)
        {
            if (block.Instructions.Any(i => i == instruction))
                return block;
        }

        return null;
    }

    private Block SplitAndCreate(Block block, int index)
    {
        if (index == 0)
            return block;

        var newBlock = new Block() { Id = _nextId++ };

        // Take the instructions for the second part
        var instructions = block.Instructions.GetRange(index, block.Instructions.Count - index);
        block.Instructions.RemoveRange(index, block.Instructions.Count - index);

        // Add those to the new block
        newBlock.Instructions.AddRange(instructions);

        // Transfer successors
        newBlock.Successors = block.Successors;

        if (block.IsDirty)
            newBlock.IsDirty = true;

        block.IsDirty = false;
        block.Successors = [];

        // Correct the predecessors for all the successors
        foreach (var successor in newBlock.Successors)
        {
            for (var i = 0; i < successor.Predecessors.Count; i++)
            {
                if (successor.Predecessors[i].Id == block.Id)
                    successor.Predecessors[i] = newBlock;
            }
        }

        // Add new block and connect it
        Blocks.Add(newBlock);
        AddDirectedEdge(block, newBlock);

        return newBlock;
    }

    private static void AddDirectedEdge(Block from, Block to)
    {
        from.Successors.Add(to);
        to.Predecessors.Add(from);
    }
}
