using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Analysis;

// Removes inlined incremental-GC write barriers.
public static class WriteBarrierRecovery
{
    private static readonly ConcurrentDictionary<ApplicationAnalysisContext, ulong> FlagAddressCache = new();

    private record Candidate(Block Guard, Block Entry, Block Merge, HashSet<Block> Region);

    public static void Run(MethodAnalysisContext method)
    {
        var flagAddress = FlagAddressCache.GetOrAdd(method.AppContext, FindBarrierFlagAddress);
        if (flagAddress == 0)
            return;

        var cfg = method.ControlFlowGraph!;

        var definitions = new Dictionary<LocalVariable, ISIL.Instruction>();
        foreach (var instruction in cfg.Instructions)
            if (instruction.Destination is LocalVariable defined)
                definitions[defined] = instruction;

        // collect every guarded barrier region first, as barriers can share their card-marking tail blocks
        var candidates = new List<Candidate>();

        foreach (var guard in cfg.Blocks.ToList())
        {
            if (!IsBarrierGuard(guard, flagAddress, definitions))
                continue;

            var first = guard.Successors[0];
            var second = guard.Successors[1];

            var candidate = TryCollectRegion(cfg, guard, first, second, definitions)
                            ?? TryCollectRegion(cfg, guard, second, first, definitions);

            if (candidate != null)
                candidates.Add(candidate);
        }

        if (candidates.Count == 0)
            return;

        bool droppedAny;
        do
        {
            droppedAny = false;
            var union = new HashSet<Block>(candidates.SelectMany(c => c.Region));
            var guards = new HashSet<Block>(candidates.Select(c => c.Guard));
            var merges = new HashSet<Block>(candidates.Select(c => c.Merge));

            for (var i = candidates.Count - 1; i >= 0; i--)
            {
                var candidate = candidates[i];

                var valid = !union.Contains(candidate.Merge) && candidate.Region.All(block =>
                    block.Predecessors.All(predecessor => guards.Contains(predecessor) || union.Contains(predecessor))
                    && block.Successors.All(successor => union.Contains(successor) || merges.Contains(successor)
                        || (successor == cfg.ExitBlock && EndsInReturn(block))));

                if (valid)
                    continue;

                candidates.RemoveAt(i);
                droppedAny = true;
            }
        } while (droppedAny && candidates.Count > 0);

        if (candidates.Count == 0)
            return;

        Excise(cfg, candidates);
        DeadCodeEliminator.Run(cfg);
    }

    private static void Excise(ISILControlFlowGraph cfg, List<Candidate> candidates)
    {
        var union = new HashSet<Block>(candidates.SelectMany(c => c.Region));

        // 1. Repair each merge's phis
        foreach (var merge in candidates.Select(c => c.Merge).Distinct())
        {
            for (var i = merge.Predecessors.Count - 1; i >= 0; i--)
            {
                if (!union.Contains(merge.Predecessors[i]))
                    continue;

                foreach (var phi in merge.Instructions)
                    if (phi.OpCode == OpCode.Phi && 1 + i < phi.Operands.Count)
                        phi.RemoveOperandAt(1 + i);

                merge.Predecessors.RemoveAt(i);
            }
        }

        // 2. Fold each guard so it goes straight to its merge.
        foreach (var (guard, entry, merge, _) in candidates)
        {
            guard.Successors.Remove(entry);
            entry.Predecessors.Remove(guard);

            var terminator = guard.Instructions[^1];
            terminator.OpCode = OpCode.Jump;
            terminator.SetOperands(merge);
            guard.CalculateBlockType();
        }

        // 3. Delete the regions.
        foreach (var block in union)
        {
            foreach (var successor in block.Successors)
                successor.Predecessors.Remove(block);
            foreach (var predecessor in block.Predecessors)
                predecessor.Successors.Remove(block);

            block.Successors.Clear();
            block.Predecessors.Clear();
            cfg.Blocks.Remove(block);
        }
    }

    private static ulong FindBarrierFlagAddress(ApplicationAnalysisContext appContext)
    {
        var barrier = appContext.GetOrCreateKeyFunctionAddresses().il2cpp_codegen_write_barrier;

        if (barrier == 0 || appContext.InstructionSet is not X86InstructionSet)
            return 0;

        try
        {
            foreach (var instruction in X86Utils.GetMethodBodyAtVirtAddressNew(barrier, false, appContext.Binary))
            {
                if (instruction is { Mnemonic: Iced.Intel.Mnemonic.Cmp, Op0Kind: Iced.Intel.OpKind.Memory }
                    && instruction.MemoryIndex == Iced.Intel.Register.None
                    && instruction.MemoryBase is Iced.Intel.Register.None or Iced.Intel.Register.RIP
                    && instruction.GetOpKind(1).IsImmediate()
                    && instruction.GetImmediate(1) == 0)
                    return instruction.IsIPRelativeMemoryOperand ? instruction.IPRelativeMemoryAddress : instruction.MemoryDisplacement64;
            }
        }
        catch
        {
            // unreadable body, just don't strip anything
        }

        return 0;
    }

    private static bool IsBarrierGuard(Block block, ulong flagAddress, Dictionary<LocalVariable, ISIL.Instruction> definitions)
    {
        if (block.BlockType != BlockType.TwoWay || block.Successors.Count != 2
            || block.Instructions.Count == 0 || block.Instructions[^1].OpCode != OpCode.ConditionalJump)
            return false;

        if (block.Instructions[^1].Operands is not [_, LocalVariable condition])
            return false;

        return IsFlagCondition(condition, flagAddress, definitions);
    }

    private static bool IsFlagCondition(LocalVariable condition, ulong flagAddress, Dictionary<LocalVariable, ISIL.Instruction> definitions) =>
        AllPathsReach(condition, definitions, (_, definition)
            => definition is { OpCode: OpCode.CheckEqual or OpCode.CheckNotEqual } compare
               && compare.Operands.Skip(1).Any(o => IsFlagValue(o, flagAddress, definitions)));

    private static bool IsFlagValue(IOperand operand, ulong flagAddress, Dictionary<LocalVariable, ISIL.Instruction> definitions) =>
        AllPathsReach(operand, definitions, (op, _) => op is MemoryOperand { IsConstant: true } memory && (ulong)memory.Addend == flagAddress);

    private static bool AllPathsReach(IOperand operand, Dictionary<LocalVariable, ISIL.Instruction> definitions, System.Func<IOperand, ISIL.Instruction?, bool> accept, Dictionary<LocalVariable, bool>? memo = null)
    {
        if (operand is not LocalVariable local)
            return accept(operand, null);

        memo ??= [];
        if (memo.TryGetValue(local, out var known))
            return known;

        if (memo.Count > 64 || !definitions.TryGetValue(local, out var definition))
            return false;

        memo[local] = false;
        var result = definition switch
        {
            { OpCode: OpCode.Move or OpCode.Not, Operands: [_, { } source] } => AllPathsReach(source, definitions, accept, memo),
            { OpCode: OpCode.Phi } => definition.Operands.Skip(1).All(input => AllPathsReach(input, definitions, accept, memo)),
            _ => accept(local, definition),
        };
        memo[local] = result;
        return result;
    }

    private static Candidate? TryCollectRegion(ISILControlFlowGraph cfg, Block guard, Block barrierEntry, Block merge, Dictionary<LocalVariable, ISIL.Instruction> definitions)
    {
        if (merge == cfg.EntryBlock || merge == cfg.ExitBlock || barrierEntry == merge || barrierEntry == guard)
            return null;

        var region = new HashSet<Block>();
        var sawReturnTail = false;
        var sawBitmapStore = false;
        var sawPageShift = false;
        var reconverges = false;

        var queue = new Queue<Block>();
        queue.Enqueue(barrierEntry);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();

            if (block == merge)
            {
                reconverges = true;
                continue;
            }

            if (block == cfg.EntryBlock || block == cfg.ExitBlock || block == guard)
                return null;

            if (!region.Add(block))
                continue;

            if (EndsInReturn(block))
            {
                if (!EndsInReturn(merge) || !TailBlockMatchesMerge(block, merge, guard, definitions))
                    return null;

                sawReturnTail = true;
                continue;
            }

            foreach (var instruction in block.Instructions)
            {
                if (!IsBarrierInstruction(instruction, ref sawBitmapStore, ref sawPageShift))
                    return null;
            }

            foreach (var successor in block.Successors)
                queue.Enqueue(successor);
        }

        // If the region doesn't contain the card-marking math (page shift + bitmap store), it's not a barrier.
        if ((!reconverges && !sawReturnTail) || !sawBitmapStore || !sawPageShift)
            return null;

        return new Candidate(guard, barrierEntry, merge, region);
    }

    private static bool EndsInReturn(Block block) 
        => block.Instructions.Count > 0 && block.Instructions[^1].OpCode == OpCode.Return;

    private static bool TailBlockMatchesMerge(Block tail, Block merge, Block guard, Dictionary<LocalVariable, ISIL.Instruction> definitions)
    {
        var tailInstructions = tail.Instructions.Where(i => i.OpCode != OpCode.Nop).ToList();
        var mergeInstructions = merge.Instructions.Where(i => i.OpCode is not (OpCode.Nop or OpCode.Phi)).ToList();

        if (tailInstructions.Count != mergeInstructions.Count)
            return false;

        for (var i = 0; i < tailInstructions.Count; i++)
        {
            var a = tailInstructions[i];
            var b = mergeInstructions[i];

            if (a.OpCode != b.OpCode || a.Operands.Count != b.Operands.Count)
                return false;

            for (var j = 0; j < a.Operands.Count; j++)
            {
                if (!EquivalentOperands(a.Operands[j], b.Operands[j], merge, guard, definitions))
                    return false;
            }
        }

        return true;
    }

    private static bool EquivalentOperands(IOperand tailOperand, IOperand mergeOperand, Block merge, Block guard, Dictionary<LocalVariable, ISIL.Instruction> definitions)
    {
        var a = ChaseCopies(tailOperand, definitions);
        var b = ResolveMergePhi(ChaseCopies(mergeOperand, definitions), merge, guard, definitions);

        return (a, b) switch
        {
            (LocalVariable la, LocalVariable lb) => ReferenceEquals(la, lb),
            (Immediate ia, Immediate ib) => ia.Value == ib.Value,
            (StringLiteral sa, StringLiteral sb) => sa.Value == sb.Value,
            (MemoryOperand ma, MemoryOperand mb) => ma.Addend == mb.Addend && ma.Scale == mb.Scale
                && EquivalentNullable(ma.Base, mb.Base, merge, guard, definitions)
                && EquivalentNullable(ma.Index, mb.Index, merge, guard, definitions),
            _ => ReferenceEquals(a, b),
        };
    }

    private static bool EquivalentNullable(IOperand? a, IOperand? b, Block merge, Block guard, Dictionary<LocalVariable, ISIL.Instruction> definitions) 
        => a == null ? b == null : b != null && EquivalentOperands(a, b, merge, guard, definitions);

    private static IOperand ResolveMergePhi(IOperand operand, Block merge, Block guard, Dictionary<LocalVariable, ISIL.Instruction> definitions)
    {
        if (operand is not LocalVariable local
            || !definitions.TryGetValue(local, out var definition)
            || definition.OpCode != OpCode.Phi
            || !merge.Instructions.Contains(definition))
            return operand;

        var guardEdge = merge.Predecessors.IndexOf(guard);
        if (guardEdge < 0 || 1 + guardEdge >= definition.Operands.Count)
            return operand;

        return ChaseCopies(definition.Operands[1 + guardEdge], definitions);
    }

    private static IOperand ChaseCopies(IOperand operand, Dictionary<LocalVariable, ISIL.Instruction> definitions)
    {
        var hops = 0;

        while (hops++ < 8 && operand is LocalVariable local
            && definitions.TryGetValue(local, out var definition)
            && definition is { OpCode: OpCode.Move, Operands: [_, LocalVariable inner] })
            operand = inner;

        return operand;
    }

    private static bool IsBarrierInstruction(ISIL.Instruction instruction, ref bool sawBitmapStore, ref bool sawPageShift)
    {
        switch (instruction.OpCode)
        {
            case OpCode.Nop:
            case OpCode.Jump:
            case OpCode.ConditionalJump: // the interlocked retry loop
                return true;

            // the dirty-bit store into the bitmap, at a computed (register-based) address
            case OpCode.Move when instruction.Operands is [MemoryOperand { Base: LocalVariable }, ..]:
                sawBitmapStore = true;
                return true;

            case OpCode.ShiftRight when instruction.Operands is [LocalVariable, _, Immediate { Value: 12 }]:
                sawPageShift = true;
                return true;

            case OpCode.Move:
            case OpCode.Phi:
            case OpCode.Add or OpCode.Subtract or OpCode.Multiply or OpCode.Divide or OpCode.Modulo:
            case OpCode.ShiftLeft or OpCode.ShiftRight:
            case OpCode.And or OpCode.Or or OpCode.Xor or OpCode.Not or OpCode.Negate:
            case >= OpCode.CheckEqual and <= OpCode.CheckLessOrEqual:
                return instruction.Operands is [LocalVariable, ..]; // computes into a local, no side effects
        }

        return false;
    }
}
