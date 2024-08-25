using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
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

    private bool TryGetTargetJumpInstructionIndex(InstructionSetIndependentInstruction instruction, out uint jumpInstructionIndex)
    {
        jumpInstructionIndex = 0;
        try
        {
            jumpInstructionIndex = ((InstructionSetIndependentInstruction)instruction.Operands[0].Data).InstructionIndex;
            return true;
        }
        catch
        {
            // ignore
        }

        return false;
    }


    public void Build(List<InstructionSetIndependentInstruction> instructions)
    {
        if (instructions == null)
            throw new ArgumentNullException(nameof(instructions));


        var currentBlock = new Block() { ID = idCounter++ };
        AddNode(currentBlock);
        AddDirectedEdge(entryBlock, currentBlock);
        for (var i = 0; i < instructions.Count; i++)
        {
            var isLast = i == instructions.Count - 1;
            switch (instructions[i].FlowControl)
            {
                case IsilFlowControl.UnconditionalJump:
                    currentBlock.AddInstruction(instructions[i]);
                    if (!isLast)
                    {
                        var newNodeFromJmp = new Block() { ID = idCounter++ };
                        AddNode(newNodeFromJmp);
                        if (TryGetTargetJumpInstructionIndex(instructions[i], out uint jumpTargetIndex))
                        {
                            // var result = instructions.Any(instruction => instruction.InstructionIndex == jumpTargetIndex);
                            currentBlock.Dirty = true;
                        }
                        else
                        {
                            AddDirectedEdge(currentBlock, exitBlock);
                        }

                        currentBlock.CaculateBlockType();
                        currentBlock = newNodeFromJmp;
                    }
                    else
                    {
                        AddDirectedEdge(currentBlock, exitBlock);
                        currentBlock.Dirty = true;
                    }

                    break;
                case IsilFlowControl.MethodCall:
                    currentBlock.AddInstruction(instructions[i]);
                    if (!isLast)
                    {
                        var newNodeFromCall = new Block() { ID = idCounter++ };
                        AddNode(newNodeFromCall);
                        AddDirectedEdge(currentBlock, newNodeFromCall);
                        currentBlock.CaculateBlockType();
                        currentBlock = newNodeFromCall;
                    }
                    else
                    {
                        AddDirectedEdge(currentBlock, exitBlock);
                        currentBlock.CaculateBlockType();
                    }

                    break;
                case IsilFlowControl.Continue:
                    currentBlock.AddInstruction(instructions[i]);
                    if (isLast)
                    {
                        // TODO: Investiage
                        /* This shouldn't happen, we've either smashed into another method or random data such as a jump table */
                    }

                    break;
                case IsilFlowControl.MethodReturn:
                    currentBlock.AddInstruction(instructions[i]);
                    if (!isLast)
                    {
                        var newNodeFromReturn = new Block() { ID = idCounter++ };
                        AddNode(newNodeFromReturn);
                        AddDirectedEdge(currentBlock, exitBlock);
                        currentBlock.CaculateBlockType();
                        currentBlock = newNodeFromReturn;
                    }
                    else
                    {
                        AddDirectedEdge(currentBlock, exitBlock);
                        currentBlock.CaculateBlockType();
                    }

                    break;
                case IsilFlowControl.ConditionalJump:
                    currentBlock.AddInstruction(instructions[i]);
                    if (!isLast)
                    {
                        var newNodeFromConditionalBranch = new Block() { ID = idCounter++ };
                        AddNode(newNodeFromConditionalBranch);
                        AddDirectedEdge(currentBlock, newNodeFromConditionalBranch);
                        currentBlock.CaculateBlockType();
                        currentBlock.Dirty = true;
                        currentBlock = newNodeFromConditionalBranch;
                    }
                    else
                    {
                        AddDirectedEdge(currentBlock, exitBlock);
                    }

                    break;
                case IsilFlowControl.Interrupt:
                    currentBlock.AddInstruction(instructions[i]);
                    var newNodeFromInterrupt = new Block() { ID = idCounter++ };
                    AddNode(newNodeFromInterrupt);
                    AddDirectedEdge(currentBlock, exitBlock);
                    currentBlock.CaculateBlockType();
                    currentBlock = newNodeFromInterrupt;
                    break;
                case IsilFlowControl.IndexedJump:
                    // This could be a part of either 2 things, a jmp to a jump table (switch statement) or a tail call to another function maybe? I dunno
                    throw new NotImplementedException("Indirect branch not implemented currently");
                default:
                    throw new NotImplementedException($"{instructions[i]} {instructions[i].FlowControl}");
            }
        }


        for (var index = 0; index < blockSet.Count; index++)
        {
            var node = blockSet[index];
            if (node.Dirty)
                FixBlock(node);
        }
    }

    public void CalculateDominations()
    {
        foreach (var block in blockSet)
        {
            throw new NotImplementedException();
        }
    }

    private void FixBlock(Block block, bool removeJmp = false)
    {
        if (block.BlockType is BlockType.Fall)
            return;

        var jump = block.isilInstructions.Last();

        var targetInstruction = jump.Operands[0].Data as InstructionSetIndependentInstruction;

        var destination = FindNodeByInstruction(targetInstruction);

        if (destination == null)
        {
            //We assume that we're tail calling another method somewhere. Need to verify if this breaks anywhere but it shouldn't in general
            block.BlockType = BlockType.Call;
            return;
        }


        int index = destination.isilInstructions.FindIndex(instruction => instruction == targetInstruction);

        var targetNode = SplitAndCreate(destination, index);

        AddDirectedEdge(block, targetNode);
        block.Dirty = false;

        if (removeJmp)
            block.isilInstructions.Remove(jump);
    }

    protected Block? FindNodeByInstruction(InstructionSetIndependentInstruction? instruction)
    {
        if (instruction == null)
            return null;

        for (var i = 0; i < blockSet.Count; i++)
        {
            var block = blockSet[i];
            for (var j = 0; j < block.isilInstructions.Count; j++)
            {
                var instr = block.isilInstructions[j];
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
        if (index < 0 || index >= target.isilInstructions.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        // Don't need to split...
        if (index == 0)
            return target;

        var newNode = new Block() { ID = idCounter++ };

        // target split in two
        // targetFirstPart -> targetSecondPart aka newNode

        // Take the instructions for the secondPart
        var instructions = target.isilInstructions.GetRange(index, target.isilInstructions.Count - index);
        target.isilInstructions.RemoveRange(index, target.isilInstructions.Count - index);

        // Add those to the newNode
        newNode.isilInstructions.AddRange(instructions);
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
                if (successor.Predecessors[i].ID == target.ID)
                    successor.Predecessors[i] = newNode;
            }
        }

        // Add newNode and connect it
        AddNode(newNode);
        AddDirectedEdge(target, newNode);

        return newNode;
    }

    private void AddDirectedEdge(Block from, Block to)
    {
        from.Successors.Add(to);
        to.Predecessors.Add(from);
    }

    protected void AddNode(Block block) => blockSet.Add(block);
}
