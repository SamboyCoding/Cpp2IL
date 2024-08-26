using System.Linq;
using System.Collections.Generic;
using Cpp2IL.Core.AST;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Graphs;

public sealed class ASTControlFlowGraph : ControlFlowGraph<Expression>
{
    private ASTControlFlowGraph()
    {
    }

    public static ASTControlFlowGraph From(ISILControlFlowGraph graph)
    {
        var astGraph = new ASTControlFlowGraph();
        var map = new Dictionary<Block<InstructionSetIndependentInstruction>, Block<Expression>>();
        foreach (var block in graph.Blocks)
        {
            map[block] = ConvertBlock(block);
        }
        foreach (var block in graph.Blocks)
        {
            map[block].Predecessors.AddRange(block.Predecessors.Select(predecessor => map[predecessor]));
            map[block].Successors.AddRange(block.Successors.Select(successor => map[successor]));
        }
        return astGraph;
    }


    private static Block<Expression> ConvertBlock(Block<InstructionSetIndependentInstruction> block)
    {
        var newBlock = new Block<Expression>
        {
            BlockType = block.BlockType
        };
        foreach (var instruction in block.Instructions)
        {
            var expression = ConvertInstruction(instruction);
            if (expression != null)
                newBlock.AddInstruction(expression);
        }
        return newBlock;
    }

    private static Expression ConvertInstruction(InstructionSetIndependentInstruction instruction)
    {
        return new Expression();
        // throw new NotImplementedException();
    }
}
