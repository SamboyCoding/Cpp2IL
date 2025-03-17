using Decompiler.ControlFlow;
using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Inlines operands where possible
/// </summary>
public class Inlining : ITransform
{
    public void Apply(Method method)
    {
        InlineLocals(method);

        // Repeat until no change
        var changed = true;
        while (changed)
            changed = InlineConstantsSinglePass(method.ControlFlowGraph);

        method.ControlFlowGraph.RemoveNops();
        method.ControlFlowGraph.RemoveEmptyBlocks();
    }

    private static bool InlineConstantsSinglePass(ControlFlowGraph graph)
    {
        var changed = false;

        var visited = new HashSet<Block>();
        var queue = new Queue<Block>();

        queue.Enqueue(graph.EntryBlock);
        visited.Add(graph.EntryBlock);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();

            for (var i = 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];

                // If it's move and it moves something to local, replace and remove it
                if (instruction.OpCode == OpCode.Move && instruction.Operands[0] is LocalVariable local)
                {
                    if (ControlFlowGraph.IsLocalUsedAfterInstruction(block, i + 1, local, out var usedByMemory))
                    {
                        // This can't be inlined into memory operand
                        if (usedByMemory) continue;

                        // Replace local
                        ReplaceLocalsUntilReassignment(block, i + 1, local, instruction.Operands[1]);

                        // Change that move to nop
                        instruction.OpCode = OpCode.Nop;
                        instruction.Operands = [];

                        changed = true;
                    }
                }
            }

            foreach (var successor in block.Successors)
            {
                if (visited.Add(successor))
                    queue.Enqueue(successor);
            }
        }

        return changed;
    }

    private static void InlineLocals(Method method)
    {
        var graph = method.ControlFlowGraph;

        var visited = new HashSet<Block>();
        var queue = new Queue<Block>();

        queue.Enqueue(graph.EntryBlock);
        visited.Add(graph.EntryBlock);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();

            for (var i = 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];

                // If it's move and it moves local to local, replace and remove it
                if (instruction.OpCode == OpCode.Move && instruction.Operands[0] is LocalVariable local && instruction.Operands[1] is LocalVariable source)
                {
                    // Replace local with source
                    ReplaceLocalsUntilReassignment(block, i + 1, local, source);

                    method.TryRemoveLocal(local);

                    // Change that move to nop
                    instruction.OpCode = OpCode.Nop;
                    instruction.Operands = [];
                }
            }

            foreach (var successor in block.Successors)
            {
                if (visited.Add(successor))
                    queue.Enqueue(successor);
            }
        }
    }

    private static void ReplaceLocalsUntilReassignment(Block block, int startIndex, LocalVariable local, object replacement)
    {
        var visited = new HashSet<Block>();
        var queue = new Queue<Block>();

        queue.Enqueue(block);
        visited.Add(block);

        while (queue.Count > 0)
        {
            var currentBlock = queue.Dequeue();

            // Use startIndex only on starting block
            for (var i = (currentBlock == block ? startIndex : 0); i < currentBlock.Instructions.Count; i++)
            {
                var instruction = currentBlock.Instructions[i];

                // Reassignment?
                if (instruction.Destination is LocalVariable destLocal && destLocal == local)
                    return;

                // Replace it
                for (var j = 0; j < instruction.Operands.Count; j++)
                {
                    var operand = instruction.Operands[j];

                    if (operand is LocalVariable usedLocal)
                    {
                        if (usedLocal == local)
                            instruction.Operands[j] = replacement;
                    }

                    if (operand is MemoryAddress memory)
                    {
                        if (memory.Base != null)
                        {
                            var baseLocal = (LocalVariable)memory.Base;

                            if (baseLocal == local)
                                memory.Base = replacement;
                        }

                        if (memory.Index != null)
                        {
                            var index = (LocalVariable)memory.Index;

                            if (index == local)
                                memory.Index = replacement;
                        }
                    }
                }
            }

            foreach (var successor in currentBlock.Successors)
            {
                if (visited.Add(successor))
                    queue.Enqueue(successor);
            }
        }
    }
}
