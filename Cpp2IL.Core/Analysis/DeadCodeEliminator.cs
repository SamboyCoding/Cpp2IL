using System.Collections.Generic;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Removes pure instructions whose result is never used. This eliminates, among other things, the
/// dead flag/temporary computations the x86 lifter emits eagerly for every comparison - a single
/// <c>cmp</c>/<c>test</c> produces all of CF/OF/SF/ZF/PF plus scratch temporaries, but the branch
/// that follows only consumes one of them.
///
/// Must run while the graph is still in SSA form (every local is assigned exactly once), so that a
/// global use count of zero is sufficient to prove a definition dead. Instructions are turned into
/// nops rather than spliced out; the structural cleanup happens later, out of SSA, where it is safe
/// for phi nodes.
/// </summary>
public static class DeadCodeEliminator
{
    public static void Run(MethodAnalysisContext method) => Run(method.ControlFlowGraph!);

    public static void Run(ISILControlFlowGraph cfg)
    {
        // Removing a dead definition can make its operands dead in turn, so iterate to a fixpoint.
        // This is monotonic (each pass only nops instructions) and therefore always terminates.
        var changed = true;
        while (changed)
        {
            changed = false;

            var useCounts = CountUses(cfg);

            foreach (var block in cfg.Blocks)
            {
                foreach (var instruction in block.Instructions)
                {
                    if (!IsRemovable(instruction.OpCode))
                        continue;

                    // Only definitions of a register local are candidates. Stores have a memory or
                    // field destination (Destination is not a local) and are never dead.
                    if (instruction.Destination is not LocalVariable destination)
                        continue;

                    if (useCounts.TryGetValue(destination, out var count) && count > 0)
                        continue;

                    instruction.OpCode = OpCode.Nop;
                    instruction.SetOperands();
                    changed = true;
                }
            }
        }
    }

    private static Dictionary<LocalVariable, int> CountUses(ISILControlFlowGraph cfg)
    {
        var counts = new Dictionary<LocalVariable, int>();

        foreach (var block in cfg.Blocks)
            foreach (var instruction in block.Instructions)
                foreach (var used in UsedLocals(instruction))
                    counts[used] = counts.TryGetValue(used, out var c) ? c + 1 : 1;

        return counts;
    }

    /// <summary>
    /// Every local read by the instruction. The single write position - a plain local destination -
    /// is excluded. Memory and field operands always contribute their address/object locals as
    /// reads, even when they are the destination of a store.
    /// </summary>
    private static IEnumerable<LocalVariable> UsedLocals(Instruction instruction)
    {
        var destination = instruction.Destination as LocalVariable;

        foreach (var operand in instruction.Operands)
        {
            switch (operand)
            {
                case LocalVariable local when !ReferenceEquals(local, destination):
                    yield return local;
                    break;
                case MemoryOperand memory:
                    if (memory.Base is LocalVariable baseLocal)
                        yield return baseLocal;
                    if (memory.Index is LocalVariable indexLocal)
                        yield return indexLocal;
                    break;
                // A static field access doesn't read the storage pointer it was resolved from, so that
                // pointer (and the class load feeding it) is free to die.
                case FieldReference { Field.IsStatic: false, Local: { } fieldLocal }:
                    yield return fieldLocal;
                    break;
                // Handing out a slot's address is a read of it as far as we can tell, whatever the callee then does with it.
                case AddressOf { Target: LocalVariable addressed }:
                    yield return addressed;
                    break;
                case AddressOf { Target: ArrayAccess addressedElement }:
                    foreach (var used in ArrayAccessLocals(addressedElement))
                        yield return used;
                    break;
                case ArrayAccess access:
                    foreach (var used in ArrayAccessLocals(access))
                        yield return used;
                    break;
                case ArrayLength { Array: { } lengthArray }:
                    yield return lengthArray;
                    break;
            }
        }
    }

    private static IEnumerable<LocalVariable> ArrayAccessLocals(ArrayAccess access)
    {
        yield return access.Array;

        if (access.Index is LocalVariable index)
            yield return index;
    }

    /// <summary>
    /// Opcodes with no side effects, so removing a never-read result is safe. Calls, stores,
    /// returns and branches are intentionally excluded.
    /// </summary>
    private static bool IsRemovable(OpCode opCode) =>
        opCode switch
        {
            OpCode.Move or OpCode.Phi
                or OpCode.Add or OpCode.Subtract or OpCode.Multiply or OpCode.Divide or OpCode.Modulo
                or OpCode.ShiftLeft or OpCode.ShiftRight
                or OpCode.And or OpCode.Or or OpCode.Xor
                or OpCode.Not or OpCode.Negate=> true,
            >= OpCode.CheckEqual and <= OpCode.CheckLessOrEqual => true,
            _ => false
        };
}
