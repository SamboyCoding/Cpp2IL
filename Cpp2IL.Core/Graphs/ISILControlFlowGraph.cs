using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Extensions;

namespace Cpp2IL.Core.Graphs;

public sealed class ISILControlFlowGraph : ControlFlowGraph<InstructionSetIndependentInstruction>
{
    private ISILControlFlowGraph()
    {
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


    public static ISILControlFlowGraph Build(List<InstructionSetIndependentInstruction> instructions)
    {
        if (instructions == null)
            throw new ArgumentNullException(nameof(instructions));


        var graph = new ISILControlFlowGraph();
        var currentBlock = new Block<InstructionSetIndependentInstruction>();
        graph.AddNode(currentBlock);
        graph.AddDirectedEdge(graph.EntryBlock, currentBlock);
        for (var i = 0; i < instructions.Count; i++)
        {
            var isLast = i == instructions.Count - 1;
            switch (instructions[i].FlowControl)
            {
                case IsilFlowControl.UnconditionalJump:
                    currentBlock.AddInstruction(instructions[i]);
                    if (!isLast)
                    {
                        var newNodeFromJmp = new Block<InstructionSetIndependentInstruction>();
                        graph.AddNode(newNodeFromJmp);
                        if (graph.TryGetTargetJumpInstructionIndex(instructions[i], out uint jumpTargetIndex))
                        {
                            // var result = instructions.Any(instruction => instruction.InstructionIndex == jumpTargetIndex);
                            currentBlock.Dirty = true;
                        }
                        else
                        {
                            graph.AddDirectedEdge(currentBlock, graph.ExitBlock);
                        }

                        currentBlock.CaculateBlockType();
                        currentBlock = newNodeFromJmp;
                    }
                    else
                    {
                        graph.AddDirectedEdge(currentBlock, graph.ExitBlock);
                        currentBlock.Dirty = true;
                    }

                    break;
                case IsilFlowControl.MethodCall:
                    currentBlock.AddInstruction(instructions[i]);
                    if (!isLast)
                    {
                        var newNodeFromCall = new Block<InstructionSetIndependentInstruction>();
                        graph.AddNode(newNodeFromCall);
                        graph.AddDirectedEdge(currentBlock, newNodeFromCall);
                        currentBlock.CaculateBlockType();
                        currentBlock = newNodeFromCall;
                    }
                    else
                    {
                        graph.AddDirectedEdge(currentBlock, graph.ExitBlock);
                        currentBlock.CaculateBlockType();
                    }

                    break;
                case IsilFlowControl.Continue:
                    currentBlock.AddInstruction(instructions[i]);
                    if (isLast)
                    {
                        // TODO: Investigate
                        /* This shouldn't happen, we've either smashed into another method or random data such as a jump table */
                    }

                    break;
                case IsilFlowControl.MethodReturn:
                    currentBlock.AddInstruction(instructions[i]);
                    if (!isLast)
                    {
                        var newNodeFromReturn = new Block<InstructionSetIndependentInstruction>();
                        graph.AddNode(newNodeFromReturn);
                        graph.AddDirectedEdge(currentBlock, graph.ExitBlock);
                        currentBlock.CaculateBlockType();
                        currentBlock = newNodeFromReturn;
                    }
                    else
                    {
                        graph.AddDirectedEdge(currentBlock, graph.ExitBlock);
                        currentBlock.CaculateBlockType();
                    }

                    break;
                case IsilFlowControl.ConditionalJump:
                    currentBlock.AddInstruction(instructions[i]);
                    if (!isLast)
                    {
                        var newNodeFromConditionalBranch = new Block<InstructionSetIndependentInstruction>();
                        graph.AddNode(newNodeFromConditionalBranch);
                        graph.AddDirectedEdge(currentBlock, newNodeFromConditionalBranch);
                        currentBlock.CaculateBlockType();
                        currentBlock.Dirty = true;
                        currentBlock = newNodeFromConditionalBranch;
                    }
                    else
                    {
                        graph.AddDirectedEdge(currentBlock, graph.ExitBlock);
                    }

                    break;
                case IsilFlowControl.Interrupt:
                    currentBlock.AddInstruction(instructions[i]);
                    var newNodeFromInterrupt = new Block<InstructionSetIndependentInstruction>();
                    graph.AddNode(newNodeFromInterrupt);
                    graph.AddDirectedEdge(currentBlock, graph.ExitBlock);
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


        for (var index = 0; index < graph.Blocks.Count; index++)
        {
            var node = graph.Blocks[index];
            if (node.Dirty)
                graph.FixBlock(node);
        }

        return graph;
    }

    private void FixBlock(Block<InstructionSetIndependentInstruction> block, bool removeJmp = false)
    {
        if (block.BlockType is BlockType.Fall)
            return;

        var jump = block.Instructions.Last();

        var targetInstruction = jump.Operands[0].Data as InstructionSetIndependentInstruction;

        var destination = FindNodeByInstruction(targetInstruction);

        if (destination == null)
        {
            // We assume that we're tail calling another method somewhere. Need to verify if this breaks anywhere but it shouldn't in general
            block.BlockType = BlockType.Call;
            return;
        }

        int index = destination.Instructions.FindIndex(instruction => instruction == targetInstruction);

        var targetNode = SplitAndCreate(destination, index);

        AddDirectedEdge(block, targetNode);
        block.Dirty = false;

        if (removeJmp)
            block.Instructions.Remove(jump);
    }

    private Block<InstructionSetIndependentInstruction>? FindNodeByInstruction(InstructionSetIndependentInstruction? instruction)
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
}
