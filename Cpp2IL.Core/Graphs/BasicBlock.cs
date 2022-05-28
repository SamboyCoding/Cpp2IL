using System.Collections.Generic;

namespace Cpp2IL.Core.Graphs;

public struct BasicBlock
{
    public BlockType BlockType;
    public List<BasicBlock> Predecessors;
    public List<BasicBlock> Successors;


    public BasicBlock(BlockType blockType)
    {
        BlockType = blockType;
        Predecessors = new();
        Successors = new();
    }
}