using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AsmResolver.Collections;
using Cpp2IL.Core.ISIL;
using Gee.External.Capstone;
using Iced.Intel;

namespace Cpp2IL.Core.Graphs;

public class Block
{
    public BlockType BlockType { get; set; }
    public List<Block> Predecessors;
    public List<Block> Successors;

    // All dominators, excluding self
    public BitList doms = new();

    // Post dominators, excluding self
    public BitList postDoms = new();

    // Dominance frontier
    public BitList domFrontier = new();

    // Immediate dominator
    public Block? idom;

    // Immediate post dominator
    public Block? iPostDom;


    public List<InstructionSetIndependentInstruction> isilInstructions;

    public int ID { get; set; }

    public bool Dirty { get; set; }
    public bool Visited = false;

    

    public override string ToString() { 
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("Type: " + BlockType);
        stringBuilder.AppendLine();
        foreach(var instruction in isilInstructions)
        {
            stringBuilder.AppendLine(instruction.ToString());
        }
        return stringBuilder.ToString();
    }

    public Block()
    {
        BlockType = BlockType.Unknown;
        Predecessors = new();
        Successors = new();
        isilInstructions = new();
        ID = -1;
    }

    public void AddInstruction(InstructionSetIndependentInstruction instruction)
    {
        isilInstructions.Add(instruction);
    }

    public void CaculateBlockType()
    {
        // This enum is kind of redundant, can be possibly swapped for IsilFlowControl and no need for BlockType?
        if (isilInstructions.Count > 0) {
            var instruction = isilInstructions.Last();
            switch (instruction.FlowControl)
            {
                case IsilFlowControl.UnconditionalJump:
                    BlockType = BlockType.OneWay;
                    break;
                case IsilFlowControl.ConditionalJump:
                    BlockType = BlockType.TwoWay;
                    break;
                case IsilFlowControl.IndexedJump:
                    BlockType = BlockType.NWay;
                    break;
                case IsilFlowControl.MethodCall:
                    BlockType = BlockType.Call;
                    break;
                case IsilFlowControl.MethodReturn:
                    BlockType = BlockType.Return;
                    break;
                case IsilFlowControl.Interrupt:
                    BlockType = BlockType.Interrupt;
                    break;
                case IsilFlowControl.Continue:
                    BlockType = BlockType.Fall;
                    break;
                default:
                    BlockType = BlockType.Unknown;
                    break;
            }

        }
    }
}
