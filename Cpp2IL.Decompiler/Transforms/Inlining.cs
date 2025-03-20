using Cpp2IL.Decompiler.ControlFlow;
using Cpp2IL.Decompiler.IL;

namespace Cpp2IL.Decompiler.Transforms;

/// <summary>
/// Inlines operands where possible
/// </summary>
public class Inlining : ITransform
{
    public void Apply(Method method, IContext context)
    {
        InlineLocals(method);

        // Repeat until no change
        var changed = true;
        while (changed)
            changed = InlineConstantsSinglePass(method.ControlFlowGraph);

        // More locals can now be inlined
        InlineLocals(method);

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
        var visited = new HashSet<(Block, int)>();

        void ProcessBlock(Block currentBlock, int index)
        {
            var key = (currentBlock, index);

            if (!visited.Add(key))
                return;

            // Process instructions starting at the given index
            for (var i = index; i < currentBlock.Instructions.Count; i++)
            {
                var instruction = currentBlock.Instructions[i];

                // Stop on this branch when reassigned
                if (instruction.Destination is LocalVariable destLocal && destLocal == local)
                    return;

                // Replace operands
                for (var j = 0; j < instruction.Operands.Count; j++)
                {
                    var operand = instruction.Operands[j];

                    if (operand is LocalVariable usedLocal && usedLocal == local)
                        instruction.Operands[j] = replacement;

                    // [base]
                    if (operand is MemoryAddress { Index: null, Addend: 0, Scale: 0 } memoryLocal)
                    {
                        if (memoryLocal.Base is LocalVariable baseLocal && baseLocal == local)
                            instruction.Operands[j] = replacement;
                    }

                    if (operand is MemoryAddress memory)
                    {
                        // [addend]
                        if (memory.IsConstant && (replacement is MemoryAddress { IsConstant: true } replacementMemory))
                            memory.Addend = replacementMemory.Addend;

                        if (memory.Base is LocalVariable baseLocal && baseLocal == local)
                            memory.Base = replacement;

                        if (memory.Index is LocalVariable indexLocal && indexLocal == local)
                            memory.Index = replacement;
                    }
                }
            }

            // Process successors
            foreach (var successor in currentBlock.Successors)
            {
                ProcessBlock(successor, 0);
            }
        }

        ProcessBlock(block, startIndex);
    }
}
