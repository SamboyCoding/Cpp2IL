using Cpp2IL.Decompiler.ControlFlow;
using Cpp2IL.Decompiler.IL;

namespace Cpp2IL.Decompiler.Transforms;

/// <summary>
/// Removes unused locals.
/// </summary>
public class RemoveUnusedLocals : ITransform
{
    public void Apply(Method method, IContext context)
    {
        var graph = method.ControlFlowGraph;

        foreach (var block in graph.Blocks)
        {
            for (var i = 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];

                // If it's move and the destination is local
                if (instruction is { Destination: LocalVariable local, IsCall: false })
                {
                    // Probably out parameter, don't remove it
                    if (method.ParameterLocals.Contains(instruction.Destination))
                        continue;

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
