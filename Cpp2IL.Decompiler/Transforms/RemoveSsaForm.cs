using Cpp2IL.Decompiler.IL;
using Cpp2IL.Decompiler.ControlFlow;

namespace Cpp2IL.Decompiler.Transforms;

/// <summary>
/// Removes phi functions and converts the method back to normal form.
/// </summary>
public class RemoveSsaForm : ITransform
{
    public void Apply(Method method, IContext context)
    {
        foreach (var block in method.ControlFlowGraph.Blocks)
        {
            // Get all phis
            var phiInstructions = block.Instructions
                .Where(i => i.OpCode == OpCode.Phi)
                .ToList();

            if (phiInstructions.Count == 0) continue;

            foreach (var predecessor in block.Predecessors)
            {
                if (predecessor.Instructions.Count == 0)
                    continue;

                predecessor.Instructions.RemoveAt(0);
                var moves = new List<Instruction>();

                foreach (var phi in phiInstructions)
                {
                    var result = (LocalVariable)phi.Operands[0]!;
                    var sources = phi.Operands.Skip(1).Cast<LocalVariable>().ToList();

                    var predIndex = block.Predecessors.IndexOf(predecessor);

                    if (predIndex < 0 || predIndex >= sources.Count)
                        continue;

                    var source = sources[predIndex];

                    // Add move for it
                    moves.Add(new Instruction(-1, OpCode.Move, result, source));
                }

                // Add all of those moves
                if (predecessor.Instructions.Count == 0)
                    predecessor.Instructions = moves;
                else
                    predecessor.Instructions.InsertRange(predecessor.Instructions.Count - (predecessor.Instructions.Count == 1 ? 1 : 2), moves);
            }

            // Remove all phis
            foreach (var instruction in block.Instructions)
            {
                if (instruction.OpCode == OpCode.Phi)
                {
                    instruction.OpCode = OpCode.Nop;
                    instruction.Operands = [];
                }
            }
        }

        method.ControlFlowGraph.RemoveNops();
        method.ControlFlowGraph.RemoveEmptyBlocks();
    }
}
