using Decompiler.ControlFlow;
using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Removes unused locals.
/// </summary>
public class RemoveUnusedLocals : ITransform
{
    public void Apply(Method method)
    {
        var graph = method.ControlFlowGraph;

        foreach (var block in graph.Blocks)
        {
            for (var i = 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];

                // If it's move and the destination is local
                if (instruction is { OpCode: OpCode.Move, Destination: LocalVariable local })
                {
                    // Is it used?
                    if (ControlFlowGraph.IsLocalUsedAfterInstruction(block, i + 1, local, out _))
                        continue;

                    // Change that move to nop
                    instruction.OpCode = OpCode.Nop;
                    instruction.Operands = [];

                    method.TryRemoveLocal(local);
                }
            }
        }

        method.ControlFlowGraph.RemoveNops();
        method.ControlFlowGraph.RemoveEmptyBlocks();
    }
}
