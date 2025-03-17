using Decompiler.ControlFlow;
using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Removes unused locals, and inlines locals where possible.
/// </summary>
public class RemoveUnusedLocalsAndInline : ITransform
{
    public void Apply(Method method)
    {
        RemoveUnusedLocals(method);
        InlineLocals(method);

        method.ControlFlowGraph.RemoveNops();
        method.ControlFlowGraph.RemoveEmptyBlocks();
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

    private static void RemoveUnusedLocals(Method method)
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
                    if (IsLocalUsedAfterInstruction(block, i + 1, local))
                        continue;

                    // Change that move to nop
                    instruction.OpCode = OpCode.Nop;
                    instruction.Operands = [];

                    method.TryRemoveLocal(local);

                    i--;
                }
            }
        }
    }

    private static void ReplaceLocalsUntilReassignment(Block block, int startIndex, LocalVariable local, LocalVariable replacement)
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
                if (instruction.OpCode == OpCode.Move)
                {
                    if (instruction.Operands[0] is LocalVariable local2 && local2 == local)
                        return;
                }

                // Replace it
                for (var j = 0; j < instruction.Operands.Count; j++)
                {
                    var operand = instruction.Operands[j];

                    if (operand is LocalVariable local2)
                    {
                        // Replace it
                        if (local2 == local)
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

    private static bool IsLocalUsedAfterInstruction(Block block, int startIndex, LocalVariable local)
    {
        for (var i = startIndex; i < block.Instructions.Count; i++)
        {
            var instruction = block.Instructions[i];

            // Instruction reads it
            if (instruction.Sources.Contains(local))
                return true;

            foreach (var source in instruction.Sources)
            {
                if (source is MemoryAddress memory && memory.Variables.Contains(local))
                    return true;
            }
        }

        return IsLocalUsedAfterBlock(block, local);
    }

    private static bool IsLocalUsedAfterBlock(Block block, LocalVariable local)
    {
        var visited = new HashSet<Block>();
        var workList = new Stack<Block>();

        workList.Push(block);
        visited.Add(block);

        while (workList.Count > 0)
        {
            var currentBlock = workList.Pop();

            // If it's not the starting block and it's used
            if (currentBlock != block && currentBlock.Use.Contains(local))
                return true;

            foreach (var successor in currentBlock.Successors)
            {
                if (visited.Add(successor))
                    workList.Push(successor);
            }
        }

        return false;
    }
}
