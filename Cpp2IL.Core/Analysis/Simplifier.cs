using System.Collections.Generic;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Post-SSA copy/constant cleanup. The bulk of copy and constant propagation is done earlier, in SSA,
/// by <see cref="SsaSimplifier"/>; this pass mops up the copies that <em>destroying</em> SSA leaves
/// behind - each phi is lowered to a <c>Move</c> on every incoming edge, so the phi's result becomes a
/// single local with one definition per predecessor.
///
/// Those multiple definitions mean a value is no longer single-assignment: at the join the definitions
/// merge, and which one reaches a use is path-dependent. So unlike <see cref="SsaSimplifier"/>, this
/// pass cannot blindly forward a definition - it walks the CFG and refuses to carry a multiply-defined
/// local's value across the join its other definitions merge at (where the phi used to be).
/// </summary>
public static class Simplifier
{
    public static void Simplify(MethodAnalysisContext method)
    {
        var cfg = method.ControlFlowGraph!;

        InlineLocals(method);

        // Repeat until no change
        var changed = true;
        while (changed)
            changed = InlineConstantsSinglePass(cfg);

        // More locals can now be inlined
        InlineLocals(method);

        cfg.RemoveNops();
        cfg.RemoveEmptyBlocks();
    }

    private static bool InlineConstantsSinglePass(ISILControlFlowGraph graph)
    {
        var changed = false;
        var definitionCounts = CountDefinitions(graph);

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
                    if (IsLocalUsedAfterInstruction(block, i + 1, local, out var usedByMemory))
                    {
                        // This can't be inlined into memory operand
                        if (usedByMemory) continue;

                        // A local with several definitions is not in SSA form, so its value at a join
                        // depends on the path taken; don't carry this definition across that join.
                        var stopAtJoins = definitionCounts.TryGetValue(local, out var defs) && defs > 1;

                        // Replace local
                        ReplaceLocalsUntilReassignment(block, i + 1, local, instruction.Operands[1], stopAtJoins);

                        // Only drop the defining move once the local has no remaining uses; if the
                        // replacement stopped at a join, the local is still live past it so the move stays.
                        if (IsLocalUsedAfterInstruction(block, i + 1, local, out _))
                            continue;

                        // Change that move to nop
                        instruction.OpCode = OpCode.Nop;
                        instruction.SetOperands();

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

    private static void InlineLocals(MethodAnalysisContext method)
    {
        var graph = method.ControlFlowGraph;
        var definitionCounts = CountDefinitions(graph!);

        var visited = new HashSet<Block>();
        var queue = new Queue<Block>();

        queue.Enqueue(graph!.EntryBlock);
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
                    // A local with several definitions is not in SSA form, so its value at a join
                    // depends on the path taken; don't carry this definition across that join.
                    var stopAtJoins = definitionCounts.TryGetValue(local, out var defs) && defs > 1;

                    // Replace local with source
                    ReplaceLocalsUntilReassignment(block, i + 1, local, source, stopAtJoins);

                    // If the replacement stopped at a join merging another definition, the local is
                    // still live there - keep its defining move rather than dropping the value on this path.
                    if (IsLocalUsedAfterInstruction(block, i + 1, local, out _))
                        continue;

                    if (!method.ParameterLocals.Contains(local))
                        method.Locals.Remove(local);

                    // Change that move to nop
                    instruction.OpCode = OpCode.Nop;
                    instruction.SetOperands();
                }
            }

            foreach (var successor in block.Successors)
            {
                if (visited.Add(successor))
                    queue.Enqueue(successor);
            }
        }
    }

    // Counts how many instructions define each local. A local with more than one definition is not in
    // SSA form: at a control-flow join its value depends on which predecessor was taken, so none of its
    // definitions may be propagated across that join - a phi would be needed there instead.
    private static Dictionary<LocalVariable, int> CountDefinitions(ISILControlFlowGraph graph)
    {
        var counts = new Dictionary<LocalVariable, int>();

        foreach (var block in graph.Blocks)
        foreach (var instruction in block.Instructions)
            if (instruction.Destination is LocalVariable local)
                counts[local] = counts.TryGetValue(local, out var count) ? count + 1 : 1;

        return counts;
    }

    private static void ReplaceLocalsUntilReassignment(Block block, int startIndex, LocalVariable local, IOperand replacement, bool stopAtJoins)
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
                    {
                        instruction.SetOperand(j, replacement);
                    }

                    // A memory operand's base/index holds an address, so only a local replacement may
                    // be substituted there (copy propagation). A constant/value replacement is left in
                    // place - the caller sees the local is still used and keeps its defining move.
                    if (operand is MemoryOperand memory && replacement is LocalVariable)
                    {
                        if (memory.Base is LocalVariable baseLocal && baseLocal == local)
                            memory.Base = replacement;

                        if (memory.Index is LocalVariable indexLocal && indexLocal == local)
                            memory.Index = replacement;

                        instruction.SetOperand(j, memory);
                    }

                    // The object a field is accessed on is an address just like a memory base.
                    if (operand is FieldReference field && replacement is LocalVariable fieldReplacement && field.Local == local)
                        field.Local = fieldReplacement;
                }
            }

            // Process successors
            foreach (var successor in currentBlock.Successors)
            {
                // A join merges this local's other definitions, so for a non-SSA (multiply-defined)
                // local the replacement must not flow past it - the value there is path-dependent.
                if (stopAtJoins && successor.Predecessors.Count > 1)
                    continue;

                ProcessBlock(successor, 0);
            }
        }

        ProcessBlock(block, startIndex);
    }

    private static bool IsLocalUsedAfterInstruction(Block block, int startIndex, LocalVariable local, out bool usedByMemory)
    {
        var visited = new HashSet<(Block, int)>();

        bool ProcessBlock(Block currentBlock, int index, out bool usedByMemory2)
        {
            usedByMemory2 = false;

            var key = (currentBlock, index);

            if (!visited.Add(key))
                return false;

            // Process instructions
            for (var i = index; i < currentBlock.Instructions.Count; i++)
            {
                var instruction = currentBlock.Instructions[i];

                // Direct usage check
                if (instruction.Sources.Contains(local))
                    return true;

                // A field access reads the object it is on, whether the field is being read or written,
                // so the destination has to be considered too - a store is not in Sources.
                foreach (var operand in instruction.Operands)
                {
                    if (operand is FieldReference field && field.Local == local)
                    {
                        usedByMemory2 = true;
                        return true;
                    }
                }

                // Used in memory operand
                foreach (var source in instruction.Sources)
                {
                    if (source is MemoryOperand memory)
                    {
                        if (memory.Base is LocalVariable memLocal && memLocal == local)
                        {
                            usedByMemory2 = true;
                            return true;
                        }

                        if (memory.Index is LocalVariable memLocal2 && memLocal2 == local)
                        {
                            usedByMemory2 = true;
                            return true;
                        }
                    }
                }
            }

            // Process successors
            foreach (var successor in currentBlock.Successors)
            {
                if (ProcessBlock(successor, 0, out usedByMemory2))
                    return true;
            }

            return false;
        }

        return ProcessBlock(block, startIndex, out usedByMemory);
    }
}
