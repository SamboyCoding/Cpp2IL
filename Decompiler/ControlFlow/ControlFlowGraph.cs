using Decompiler.IL;

namespace Decompiler.ControlFlow;

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
    public List<Instruction> AllInstructions => GetAllInstructions();

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
                case OpCode.TailCall:
                case OpCode.Unknown:
                    currentBlock.AddInstruction(instruction);

                    if (!isLast)
                    {
                        var newBlock = new Block() { Id = graph._nextId++ };
                        graph.Blocks.Add(newBlock);

                        if (instruction.OpCode == OpCode.TailCall)
                            AddDirectedEdge(currentBlock, graph.ExitBlock);
                        else
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

        graph.ConnectBlocksWithoutSuccessorsToExit();

        foreach (var block in graph.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                if (instruction.Operands.Count > 0 && instruction.Operands[0] is BranchTargetInstruction target)
                    instruction.Operands[0] = new BranchTargetBlock(graph.GetBlockByInstruction(target.Instruction)!);
            }
        }

        return graph;
    }

    // I don't know why this even happens
    private void ConnectBlocksWithoutSuccessorsToExit()
    {
        var visited = new HashSet<Block>();
        var queue = new Queue<Block>();

        queue.Enqueue(EntryBlock);
        visited.Add(EntryBlock);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();

            if (block.Successors.Count == 0 && block != EntryBlock && block != ExitBlock)
                AddDirectedEdge(block, ExitBlock);

            foreach (var successor in block.Successors)
            {
                if (visited.Add(successor))
                    queue.Enqueue(successor);
            }
        }
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
            Blocks.RemoveAt(i + 1);
            i--;
        }
    }

    /// <summary>
    /// Removes all nop instructions from the graph.
    /// </summary>
    public void RemoveNops()
    {
        var visited = new HashSet<Block>();
        var queue = new Queue<Block>();

        queue.Enqueue(EntryBlock);
        visited.Add(EntryBlock);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();

            for (var i = 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];
                if (instruction.OpCode != OpCode.Nop) continue;
                block.Instructions.RemoveAt(i);
                i--;
            }

            foreach (var successor in block.Successors)
            {
                if (visited.Add(successor))
                    queue.Enqueue(successor);
            }
        }

        RemoveEmptyBlocks();
    }

    /// <summary>
    /// Removes all blocks that don't have any instructions.
    /// </summary>
    public void RemoveEmptyBlocks()
    {
        var emptyBlocks = Blocks.Where(b => b.Instructions.Count == 0).ToList();

        foreach (var block in emptyBlocks)
        {
            if (block == EntryBlock || block == ExitBlock)
                continue;

            foreach (var pred in block.Predecessors)
            {
                pred.Successors.Remove(block);
                pred.Successors.AddRange(block.Successors);
            }

            foreach (var succ in block.Successors)
            {
                succ.Predecessors.Remove(block);
                succ.Predecessors.AddRange(block.Predecessors);
            }

            Blocks.Remove(block);
        }

        foreach (var block in Blocks)
        {
            block.Successors = block.Successors.Distinct().ToList();
            block.Predecessors = block.Predecessors.Distinct().ToList();
        }
    }

    private List<Instruction> GetAllInstructions()
    {
        var instructions = new List<Instruction>();
        var visited = new HashSet<Block>();
        var queue = new Queue<Block>();

        queue.Enqueue(EntryBlock);
        visited.Add(EntryBlock);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();
            instructions.AddRange(block.Instructions);

            foreach (var successor in block.Successors)
            {
                if (visited.Add(successor))
                    queue.Enqueue(successor);
            }
        }

        return instructions;
    }

    private void SplitTargetBlock(Block block)
    {
        if (block.IsFallThrough)
            return;

        // Get the branch target block
        var branch = block.Instructions.Last();
        var target = (BranchTargetInstruction)branch.Operands[0]!;
        var targetBlock = GetBlockByInstruction(target.Instruction);

        // Split it at the target instruction
        var index = targetBlock!.Instructions.FindIndex(i => i == target!.Instruction);
        var targetBlock2 = SplitAndCreate(targetBlock, index);
        AddDirectedEdge(block, targetBlock2);

        block.IsDirty = false;
    }

    public Block? GetBlockByInstruction(Instruction instruction)
    {
        foreach (var block in Blocks)
        {
            if (block.Instructions.Any(i => i == instruction))
                return block;
        }

        return null;
    }

    private Block SplitAndCreate(Block target, int index)
    {
        if (index == 0)
            return target;

        var newBlock = new Block() { Id = _nextId++ };

        // Take the instructions for the second part
        var instructions = target.Instructions.GetRange(index, target.Instructions.Count - index);
        target.Instructions.RemoveRange(index, target.Instructions.Count - index);

        // Add those to the newNode
        newBlock.Instructions.AddRange(instructions);

        // Transfer successors
        newBlock.Successors = target.Successors;

        if (target.IsDirty)
            newBlock.IsDirty = true;

        target.IsDirty = false;
        target.Successors = [];

        // Correct the predecessors for all the successors
        foreach (var successor in newBlock.Successors)
        {
            for (var i = 0; i < successor.Predecessors.Count; i++)
            {
                if (successor.Predecessors[i].Id == target.Id)
                    successor.Predecessors[i] = newBlock;
            }
        }

        // Add new block and connect it
        Blocks.Add(newBlock);
        AddDirectedEdge(target, newBlock);

        return newBlock;
    }

    private static void AddDirectedEdge(Block from, Block to)
    {
        from.Successors.Add(to);
        to.Predecessors.Add(from);
    }
}
