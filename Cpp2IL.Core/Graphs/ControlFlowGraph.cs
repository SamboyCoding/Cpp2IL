using System;
using System.Collections.ObjectModel;

namespace Cpp2IL.Core.Graphs;

public abstract class ControlFlowGraph<Instruction> where Instruction : notnull
{
    public Collection<Block<Instruction>> Blocks => blockSet;
    public Block<Instruction> EntryBlock => entryBlock;
    public Block<Instruction> ExitBlock => exitBlock;

    private Block<Instruction> exitBlock;
    private Block<Instruction> entryBlock;

    private Collection<Block<Instruction>> blockSet;

    public int Count => blockSet.Count;

    public ControlFlowGraph()
    {
        entryBlock = new Block<Instruction>() { BlockType = BlockType.Entry };
        exitBlock = new Block<Instruction>() { BlockType = BlockType.Exit };
        blockSet =
        [
            entryBlock,
            exitBlock
        ];
    }

    public void AddDirectedEdge(Block<Instruction> from, Block<Instruction> to)
    {
        from.Successors.Add(to);
        to.Predecessors.Add(from);
    }

    public void AddNode(Block<Instruction> block) => blockSet.Add(block);


    public Block<Instruction> SplitAndCreate(Block<Instruction> target, int index)
    {
        if (index < 0 || index >= target.Instructions.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        // Don't need to split...
        if (index == 0)
            return target;

        var newNode = new Block<Instruction>();

        // target split in two
        // targetFirstPart -> targetSecondPart aka newNode

        // Take the instructions for the secondPart
        var instructions = target.Instructions.GetRange(index, target.Instructions.Count - index);
        target.Instructions.RemoveRange(index, target.Instructions.Count - index);

        // Add those to the newNode
        newNode.Instructions.AddRange(instructions);
        // Transfer control flow
        newNode.BlockType = target.BlockType;
        target.BlockType = BlockType.Fall;

        // Transfer successors
        newNode.Successors = target.Successors;
        if (target.Dirty)
            newNode.Dirty = true;
        target.Dirty = false;
        target.Successors = [];

        // Correct the predecessors for all the successors
        foreach (var successor in newNode.Successors)
        {
            for (int i = 0; i < successor.Predecessors.Count; i++)
            {
                if (successor.Predecessors[i] == target)
                    successor.Predecessors[i] = newNode;
            }
        }

        // Add newNode and connect it
        AddNode(newNode);
        AddDirectedEdge(target, newNode);

        return newNode;
    }
}
