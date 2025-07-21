using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Graphs;

public class ISILControlFlowGraph
{
    public Block EntryBlock => entryBlock;
    public Block ExitBlock => exitBlock;
    public int Count => blockSet.Count;
    public Collection<Block> Blocks => blockSet;

    private int idCounter;
    private Collection<Block> blockSet;
    private Block exitBlock;
    private Block entryBlock;

    public ISILControlFlowGraph()
    {
        entryBlock = new Block() { ID = idCounter++ };
        entryBlock.BlockType = BlockType.Entry;
        exitBlock = new Block() { ID = idCounter++ };
        exitBlock.BlockType = BlockType.Exit;
        blockSet =
        [
            entryBlock,
            exitBlock
        ];
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
        // Get reachable blocks
        var reachable = new HashSet<Block>();
        var worklist = new Queue<Block>();
        worklist.Enqueue(EntryBlock);

        while (worklist.Count > 0)
        {
            var block = worklist.Dequeue();

            if (!reachable.Add(block))
                continue;

            foreach (var succ in block.Successors)
                worklist.Enqueue(succ);
        }

        // Remove unreachable blocks
        var toRemove = blockSet.Where(b => !reachable.Contains(b)).ToList();
        foreach (var block in toRemove)
        {
            foreach (var pred in block.Predecessors)
                pred.Successors.Remove(block);

            foreach (var succ in block.Successors)
                succ.Predecessors.Remove(block);

            blockSet.Remove(block);
        }
    }

    public void Build(List<Instruction> instructions)
    {
        if (instructions == null)
            throw new ArgumentNullException(nameof(instructions));

        var currentBlock = new Block() { ID = idCounter++ };
        AddBlock(currentBlock);
        AddDirectedEdge(entryBlock, currentBlock);

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
                                AddDirectedEdge(currentBlock, exitBlock);
                        }
                        else
                        {
                            AddDirectedEdge(currentBlock, newBlock);
                            currentBlock.Dirty = true;
                        }

                        currentBlock.CaculateBlockType();
                        currentBlock = newBlock;
                    }
                    else
                    {
                        AddDirectedEdge(currentBlock, exitBlock);

                        if (instructions[i].OpCode == OpCode.Jump)
                            currentBlock.Dirty = true;
                    }

                    break;

                case OpCode.Call:
                case OpCode.CallVoid:
                case OpCode.Return:
                case OpCode.ReturnVoid:
                    var isReturn = instructions[i].OpCode == OpCode.Return || instructions[i].OpCode == OpCode.ReturnVoid;

                    currentBlock.AddInstruction(instructions[i]);

                    if (!isLast)
                    {
                        newBlock = new Block() { ID = idCounter++ };
                        AddBlock(newBlock);
                        AddDirectedEdge(currentBlock, isReturn ? exitBlock : newBlock);
                        currentBlock.CaculateBlockType();
                        currentBlock = newBlock;
                    }
                    else
                    {
                        AddDirectedEdge(currentBlock, exitBlock);
                        currentBlock.CaculateBlockType();
                    }

                    break;

                default:
                    currentBlock.AddInstruction(instructions[i]);
                    if (isLast)
                    {
                        AddDirectedEdge(currentBlock, exitBlock);
                        currentBlock.CaculateBlockType();
                    }
                    break;
            }
        }

        for (var index = 0; index < blockSet.Count; index++)
        {
            var node = blockSet[index];
            if (node.Dirty)
                FixBlock(node);
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

    protected Block? FindBlockByInstruction(Instruction? instruction)
    {
        if (instruction == null)
            return null;

        for (var i = 0; i < blockSet.Count; i++)
        {
            var block = blockSet[i];
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

    protected void AddBlock(Block block) => blockSet.Add(block);
}
