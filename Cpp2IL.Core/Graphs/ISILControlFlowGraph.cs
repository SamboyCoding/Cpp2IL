using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Graphs;

public class ISILControlFlowGraph
{
    public Block EntryBlock;
    public Block ExitBlock;
    public int Count => Blocks.Count;
    public List<Block> Blocks;

    public List<Instruction> Instructions
    {
        get
        {
            // BFS search
            var visited = new HashSet<Block>();
            var queue = new Queue<Block>();
            var result = new List<Instruction>();

            queue.Enqueue(EntryBlock);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (!visited.Add(current))
                    continue;

                result.AddRange(current.Instructions);

                foreach (var successor in current.Successors)
                {
                    if (!visited.Contains(successor))
                        queue.Enqueue(successor);
                }
            }

            return result; // Should this be cached?
        }
    }

    private int idCounter;

    public ISILControlFlowGraph(List<Instruction> instructions)
    {
        EntryBlock = new Block
        {
            ID = idCounter++,
            BlockType = BlockType.Entry
        };

        ExitBlock = new Block
        {
            ID = idCounter++,
            BlockType = BlockType.Exit
        };

        Blocks =
        [
            EntryBlock,
            ExitBlock
        ];

        Build(instructions);
    }

    private bool TryGetTargetJumpInstructionIndex(Instruction instruction, out int jumpInstructionIndex)
    {
        jumpInstructionIndex = 0;
        try
        {
            jumpInstructionIndex = ((Instruction)instruction.Operands[0]).Index;
            return true;
        }
        catch
        {
            // ignore
        }

        return false;
    }

    public void RemoveUnreachableBlocks()
    {
        if (Blocks.Count == 0)
            return;

        // Get blocks reachable from entry
        var reachable = new List<Block>();
        var visited = new List<Block> { EntryBlock };
        reachable.Add(EntryBlock);

        var total = 0;
        while (total < reachable.Count)
        {
            var block = reachable[total];
            total++;

            foreach (var successor in block.Successors)
            {
                if (visited.Contains(successor))
                    continue;
                visited.Add(successor);
                reachable.Add(successor);
            }
        }

        // Get unreachable blocks
        var unreachable = Blocks.Where(block => !visited.Remove(block)).ToList();

        // Remove those
        foreach (var block in unreachable)
        {
            // Don't remove entry or exit
            if (block == EntryBlock || block == ExitBlock)
                continue;

            block.Successors.Clear();
            Blocks.Remove(block);
        }
    }

    public void RemoveNops()
    {
        var usedAsTarget = new HashSet<Instruction>();

        // Get all instructions used as branch targets
        foreach (var block in Blocks)
        {
            foreach (var instr in block.Instructions)
            {
                foreach (var operand in instr.Operands)
                {
                    if (operand is Instruction target)
                        usedAsTarget.Add(target);
                }
            }
        }

        // Build replacement map for NOPs that are safe to replace
        var instructionReplacement = new Dictionary<Instruction, Instruction>();
        foreach (var block in Blocks)
        {
            Instruction? replacement = null;
            for (var i = block.Instructions.Count - 1; i >= 0; i--)
            {
                var instr = block.Instructions[i];
                if (instr.OpCode == OpCode.Nop)
                {
                    if (replacement != null && !usedAsTarget.Contains(instr))
                        instructionReplacement[instr] = replacement;
                }
                else
                {
                    replacement = instr;
                }
            }
        }

        // Update operands
        foreach (var block in Blocks)
        {
            foreach (var instr in block.Instructions)
            {
                for (var i = 0; i < instr.Operands.Count; i++)
                {
                    if (instr.Operands[i] is Instruction target && instructionReplacement.TryGetValue(target, out var newTarget))
                        instr.Operands[i] = newTarget;
                }
            }
        }

        // Remove NOPs
        foreach (var block in Blocks)
        {
            block.Instructions.RemoveAll(i => i.OpCode == OpCode.Nop && !usedAsTarget.Contains(i));
        }
    }

    public void RemoveEmptyBlocks()
    {
        var toRemove = new List<Block>();

        foreach (var block in Blocks)
        {
            if (block == EntryBlock || block == ExitBlock)
                continue;

            if (block.Instructions.Count == 0)
            {
                // Redirect predecessors to successors
                foreach (var pred in block.Predecessors)
                {
                    pred.Successors.Remove(block);
                    foreach (var succ in block.Successors)
                    {
                        if (!pred.Successors.Contains(succ))
                            pred.Successors.Add(succ);
                    }
                }

                // Redirect successors to predecessors
                foreach (var succ in block.Successors)
                {
                    succ.Predecessors.Remove(block);
                    foreach (var pred in block.Predecessors)
                    {
                        if (!succ.Predecessors.Contains(pred))
                            succ.Predecessors.Add(pred);
                    }
                }

                toRemove.Add(block);
            }
        }

        foreach (var block in toRemove)
            Blocks.Remove(block);
    }

    public void BuildUseDefLists()
    {
        foreach (var block in Blocks)
        {
            var use = new List<object>();
            var def = new List<object>();

            foreach (var instruction in block.Instructions)
            {
                foreach (var operand in instruction.Sources.Where(operand => !use.Contains(operand)))
                    use.Add(operand);

                if (instruction.Destination != null && !def.Contains(instruction.Destination))
                    def.Add(instruction.Destination);
            }

            block.Use = use;
            block.Def = def;
        }
    }

    public void MergeCallBlocks()
    {
        var toRemove = new List<Block>();

        for (var i = 0; i < Blocks.Count; i++)
        {
            var block = Blocks[i];
            if (block.BlockType != BlockType.Call) continue;

            if (block.Successors.Count != 1)
                continue;

            var nextBlock = block.Successors[0];

            // make sure that the next block only has one predecessor (this)
            if (nextBlock.Predecessors.Count != 1 || nextBlock.Predecessors[0] != block)
                continue;

            // merge instructions
            block.Instructions.AddRange(nextBlock.Instructions);
            block.Successors = nextBlock.Successors;

            // fix up successors predecessors
            foreach (var successor in nextBlock.Successors)
            {
                for (var j = 0; j < successor.Predecessors.Count; j++)
                {
                    if (successor.Predecessors[j] == nextBlock)
                        successor.Predecessors[j] = block;
                }
            }

            toRemove.Add(nextBlock);
        }

        // Remove all merged blocks
        foreach (var removed in toRemove)
            Blocks.Remove(removed);

        foreach (var block in Blocks)
            block.CalculateBlockType();
    }

    private void Build(List<Instruction> instructions)
    {
        if (instructions == null)
            throw new ArgumentNullException(nameof(instructions));

        var currentBlock = new Block() { ID = idCounter++ };
        AddBlock(currentBlock);
        AddDirectedEdge(EntryBlock, currentBlock);

        for (var i = 0; i < instructions.Count; i++)
        {
            var isLast = i == instructions.Count - 1;
            Block newBlock;

            switch (instructions[i].OpCode)
            {
                case OpCode.Jump:
                case OpCode.ConditionalJump:
                    currentBlock.AddInstruction(instructions[i]);

                    if (!isLast)
                    {
                        newBlock = new Block() { ID = idCounter++ };
                        AddBlock(newBlock);

                        if (instructions[i].OpCode == OpCode.Jump)
                        {
                            if (TryGetTargetJumpInstructionIndex(instructions[i], out int jumpTargetIndex))
                                currentBlock.Dirty = true;
                            else
                                AddDirectedEdge(currentBlock, ExitBlock);
                        }
                        else
                        {
                            AddDirectedEdge(currentBlock, newBlock);
                            currentBlock.Dirty = true;
                        }

                        currentBlock.CalculateBlockType();
                        currentBlock = newBlock;
                    }
                    else
                    {
                        AddDirectedEdge(currentBlock, ExitBlock);

                        if (instructions[i].OpCode == OpCode.Jump)
                            currentBlock.Dirty = true;
                    }

                    break;

                case OpCode.Call:
                case OpCode.CallVoid:
                case OpCode.Return:
                    var isReturn = instructions[i].OpCode == OpCode.Return;

                    currentBlock.AddInstruction(instructions[i]);

                    if (!isLast)
                    {
                        newBlock = new Block() { ID = idCounter++ };
                        AddBlock(newBlock);
                        AddDirectedEdge(currentBlock, isReturn ? ExitBlock : newBlock);
                        currentBlock.CalculateBlockType();
                        currentBlock = newBlock;
                    }
                    else
                    {
                        AddDirectedEdge(currentBlock, ExitBlock);
                        currentBlock.CalculateBlockType();
                    }

                    break;

                default:
                    currentBlock.AddInstruction(instructions[i]);
                    if (isLast)
                    {
                        AddDirectedEdge(currentBlock, ExitBlock);
                        currentBlock.CalculateBlockType();
                    }
                    break;
            }
        }

        for (var index = 0; index < Blocks.Count; index++)
        {
            var node = Blocks[index];
            if (node.Dirty)
                FixBlock(node);
        }

        // Connect blocks without successors to exit
        foreach (var block in Blocks)
        {
            if (block.Successors.Count == 0 && block != EntryBlock && block != ExitBlock)
                AddDirectedEdge(block, ExitBlock);
        }

        // Change branch targets to blocks
        foreach (var instruction in Blocks.SelectMany(block => block.Instructions))
        {
            if (instruction.Operands.Count > 0 && instruction.Operands[0] is Instruction target)
                instruction.Operands[0] = FindBlockByInstruction(target)!;
        }
    }

    private void FixBlock(Block block, bool removeJmp = false)
    {
        if (block.BlockType is BlockType.Fall)
            return;

        var jump = block.Instructions.Last();

        var targetInstruction = jump.Operands[0] as Instruction;

        var destination = FindBlockByInstruction(targetInstruction);

        if (destination == null)
        {
            //We assume that we're tail calling another method somewhere. Need to verify if this breaks anywhere but it shouldn't in general
            block.BlockType = BlockType.TailCall;
            return;
        }


        int index = destination.Instructions.FindIndex(instruction => instruction == targetInstruction);

        var targetNode = SplitAndCreate(destination, index);

        AddDirectedEdge(block, targetNode);
        block.Dirty = false;

        if (removeJmp)
            block.Instructions.Remove(jump);
    }

    public Block? FindBlockByInstruction(Instruction? instruction)
    {
        if (instruction == null)
            return null;

        for (var i = 0; i < Blocks.Count; i++)
        {
            var block = Blocks[i];
            for (var j = 0; j < block.Instructions.Count; j++)
            {
                var instr = block.Instructions[j];
                if (instr == instruction)
                {
                    return block;
                }
            }
        }

        return null;
    }

    private Block SplitAndCreate(Block target, int index)
    {
        if (index < 0 || index >= target.Instructions.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        // Don't need to split...
        if (index == 0)
            return target;

        var newBlock = new Block() { ID = idCounter++ };

        // target split in two
        // targetFirstPart -> targetSecondPart aka newNode

        // Take the instructions for the secondPart
        var instructions = target.Instructions.GetRange(index, target.Instructions.Count - index);
        target.Instructions.RemoveRange(index, target.Instructions.Count - index);

        // Add those to the newNode
        newBlock.Instructions.AddRange(instructions);
        // Transfer control flow
        newBlock.BlockType = target.BlockType;
        target.BlockType = BlockType.Fall;

        // Transfer successors
        newBlock.Successors = target.Successors;
        if (target.Dirty)
            newBlock.Dirty = true;
        target.Dirty = false;
        target.Successors = [];

        // Correct the predecessors for all the successors
        foreach (var successor in newBlock.Successors)
        {
            for (int i = 0; i < successor.Predecessors.Count; i++)
            {
                if (successor.Predecessors[i].ID == target.ID)
                    successor.Predecessors[i] = newBlock;
            }
        }

        // Add newNode and connect it
        AddBlock(newBlock);
        AddDirectedEdge(target, newBlock);

        return newBlock;
    }

    private void AddDirectedEdge(Block from, Block to)
    {
        from.Successors.Add(to);
        to.Predecessors.Add(from);
    }

    protected void AddBlock(Block block) => Blocks.Add(block);
}
