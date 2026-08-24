using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

// Points branches whose condition folded to a constant at the block they actually reach.
public static class ConstantBranchFolder
{
    public static void Run(MethodAnalysisContext method)
    {
        var graph = method.ControlFlowGraph!;

        foreach (var block in graph.Blocks)
        {
            if (block.Instructions.Count == 0)
                continue;

            var branch = block.Instructions[^1];

            if (branch is not { OpCode: OpCode.ConditionalJump, Operands: [_, Immediate condition] })
                continue;

            if (ResolveTarget(branch, graph) is not { } taken)
                continue;

            var destination = condition.Value != 0
                ? taken
                : block.Successors.FirstOrDefault(s => s != taken && s != graph.ExitBlock);

            if (destination == null)
                continue;

            branch.OpCode = OpCode.Jump;
            branch.SetOperands(destination);
        }
    }

    private static Block? ResolveTarget(Instruction branch, ISILControlFlowGraph graph) => branch.Operands[0] switch
    {
        Block block => block,
        Instruction instruction => graph.FindBlockByInstruction(instruction),
        _ => null,
    };
}
