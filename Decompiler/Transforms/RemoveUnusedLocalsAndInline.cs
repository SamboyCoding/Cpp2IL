using Decompiler.ControlFlow;
using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Removes unused locals and inlines those where possible.
/// </summary>
public class RemoveUnusedLocalsAndInline : ITransform
{
    public void Apply(Method method)
    {
        RemoveUnusedLocals(method);
        InlineLocals(method);
        method.ControlFlowGraph.Simplify();
    }

    private static void InlineLocals(Method method)
    {
        var graph = method.ControlFlowGraph;

        // BFS search
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

                    // If it's not param, remove it from locals
                    if (!method.ParameterLocals.Contains(local))
                        method.Locals.Remove(local);

                    // Remove that move instruction
                    block.Instructions.RemoveAt(i);
                    i--;
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

        // BFS search
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

                // If it's move and first operand is local
                if (instruction.OpCode == OpCode.Move && instruction.Operands[0] is LocalVariable local)
                {
                    // Is it used?
                    if (IsLocalUsedAfterInstruction(block, i + 1, local))
                        continue;

                    // Remove the move
                    block.Instructions.RemoveAt(i);

                    // If it's not param, remove it from locals
                    if (!method.ParameterLocals.Contains(local))
                        method.Locals.Remove(local);

                    i--;
                }
            }

            foreach (var successor in block.Successors)
            {
                if (visited.Add(successor))
                    queue.Enqueue(successor);
            }
        }
    }

    private static void ReplaceLocalsUntilReassignment(Block block, int startIndex, LocalVariable local, LocalVariable replacement)
    {
        // BFS search
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
                    if ((LocalVariable)instruction.Operands[0]! == local)
                        return;
                }

                // Replace it
                ReplaceSingleInstruction(instruction);
            }

            foreach (var successor in currentBlock.Successors)
            {
                if (visited.Add(successor))
                    queue.Enqueue(successor);
            }
        }

        return;

        void ReplaceSingleInstruction(Instruction instruction)
        {
            for (var j = 0; j < instruction.Operands.Count; j++)
            {
                var operand = instruction.Operands[j];

                if (operand is LocalVariable local2)
                {
                    // Replace it
                    if (local2 == local)
                        instruction.Operands[j] = replacement;
                }

                // Nested instruction
                if (operand is Instruction instructionOp)
                    ReplaceSingleInstruction(instructionOp);
            }
        }
    }

    private static bool IsLocalUsedAfterInstruction(Block block, int startIndex, LocalVariable local)
    {
        for (var i = startIndex; i < block.Instructions.Count; i++)
        {
            var instruction = block.Instructions[i];
            // Instruction reads it
            if (instruction.ReadOperands.Contains(local))
                return true;
        }

        return IsLocalUsedAfterBlock(block, local);
    }

    private static bool IsLocalUsedAfterBlock(Block block, LocalVariable local)
    {
        // BFS search
        var visited = new HashSet<Block>();
        var queue = new Queue<Block>();

        queue.Enqueue(block);
        visited.Add(block);

        while (queue.Count > 0)
        {
            var currentBlock = queue.Dequeue();

            // If it's not the starting block and it's used
            if (currentBlock != block && currentBlock.Use.Contains(local))
                return true;

            foreach (var successor in currentBlock.Successors)
            {
                if (visited.Add(successor))
                    queue.Enqueue(successor);
            }
        }

        return false;
    }
}
