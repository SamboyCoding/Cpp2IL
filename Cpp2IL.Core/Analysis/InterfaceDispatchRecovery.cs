using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Analysis;

// Recovers interface calls from GetInterfaceInvokeData which is usually inlined. that method scans klass->interfaceOffsets
// for the declaring interface, indexes the vtable with (entryOffset + slot), or falls back to a slow path
// helper when the scan fails.
public static class InterfaceDispatchRecovery
{
    public static void Run(MethodAnalysisContext method)
    {
        // offsets below are the 64-bit Il2CppClass layout
        if (method.AppContext.Binary.PointerSizeBytes != 8)
            return;

        var cfg = method.ControlFlowGraph!;

        var definitions = new Dictionary<LocalVariable, Instruction>();
        var homeBlock = new Dictionary<Instruction, Block>();

        foreach (var block in cfg.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                homeBlock[instruction] = block;
                if (instruction.Destination is LocalVariable destination)
                    definitions[destination] = instruction;
            }
        }

        var changed = false;

        foreach (var block in cfg.Blocks.ToList())
        {
            foreach (var instruction in block.Instructions.ToList())
            {
                if (instruction.OpCode is not (OpCode.IndirectCall or OpCode.IndirectJump))
                    continue;

                if (MatchDispatch(method, instruction, definitions, homeBlock) is not { } match)
                    continue;

                RewriteDispatch(method, instruction, block, match, definitions);
                TryExciseLookup(cfg, match, definitions, homeBlock);
                changed = true;
            }
        }

        if (changed)
            DeadCodeEliminator.Run(method);
    }

    private const long VTableOffset = 0x138;
    private const int InvokeDataShift = 4; // sizeof(VirtualInvokeData) == 16

    private record struct Match(
        MethodAnalysisContext Resolved,
        Instruction InvokeDataPhi,
        Block Merge,
        Instruction SlowCall,
        LocalVariable KlassLocal);

    private static Match? MatchDispatch(MethodAnalysisContext method, Instruction dispatch, Dictionary<LocalVariable, Instruction> definitions, Dictionary<Instruction, Block> homeBlock)
    {
        // the call target loads VirtualInvokeData::methodPtr, separately or folded in
        var targetLoad = dispatch.Operands[0] switch
        {
            MemoryOperand folded => folded,
            LocalVariable target when Definition(definitions, target) is { OpCode: OpCode.Move, Operands: [_, MemoryOperand loaded] } => loaded,
            _ => default(MemoryOperand?)
        };

        if (targetLoad is not { Index: null, Scale: 0, Addend: 0, Base: LocalVariable invokeData })
            return null;

        if (Definition(definitions, invokeData) is not { OpCode: OpCode.Phi, Operands: [_, LocalVariable first, LocalVariable second] } phi)
            return null;

        var firstDefinition = Definition(definitions, first);
        var secondDefinition = Definition(definitions, second);

        var slowCall = firstDefinition is { OpCode: OpCode.Call } 
            ? firstDefinition
            : secondDefinition is { OpCode: OpCode.Call } ? secondDefinition : null;
        var vtableEntry = ReferenceEquals(slowCall, firstDefinition) ? secondDefinition : firstDefinition;

        // slow path is GetInterfaceInvokeDataFromVTableSlowPath(obj, interface, slot), never resolved
        if (slowCall is not { Operands: [Immediate, _, _, LocalVariable interfaceArg, LocalVariable slotArg, ..] })
            return null;

        if (ChaseCopies(definitions, interfaceArg) is not { OpCode: OpCode.Move, Operands: [_, TypeAnalysisContext declaringInterface] })
            return null;

        if (declaringInterface is RuntimeMethodInfoAnalysisContext
            || !(declaringInterface is GenericInstanceTypeAnalysisContext { GenericType.IsInterface: true } || declaringInterface.IsInterface))
            return null;

        if (ChaseCopies(definitions, slotArg) is not { OpCode: OpCode.Move, Operands: [_, Immediate slotImmediate] }
            || slotImmediate.Value is < 0 or > ushort.MaxValue)
            return null;

        var slot = (int)slotImmediate.Value;

        // fast path computes klass + vtableOffset + ((entryOffset + slot) << 4) (the +slot folds away for slot 0)
        if (MatchVTableEntryChain(definitions, vtableEntry, slot) is not { } klassLocal)
            return null;

        if (ResolveInterfaceSlot(declaringInterface, slot) is not { } resolved)
            return null;

        if (!homeBlock.TryGetValue(phi, out var merge))
            return null;

        return new Match(resolved, phi, merge, slowCall, klassLocal);
    }

    private static LocalVariable? MatchVTableEntryChain(Dictionary<LocalVariable, Instruction> definitions, Instruction? vtableEntry, int slot)
    {
        if (vtableEntry is not { OpCode: OpCode.Add, Operands: [_, LocalVariable addLeft, LocalVariable addRight] })
            return null;

        var (klassCandidate, sum) = Definition(definitions, addRight) is { OpCode: OpCode.Move, Operands: [_, MemoryOperand { Index: null, Scale: 0, Addend: 0 }] }
            ? (addRight, addLeft)
            : (addLeft, addRight);

        if (Definition(definitions, klassCandidate) is not { OpCode: OpCode.Move, Operands: [_, MemoryOperand { Index: null, Scale: 0, Addend: 0, Base: LocalVariable }] })
            return null;

        if (ChaseCopies(definitions, sum) is not { OpCode: OpCode.Add, Operands: [_, LocalVariable shifted, Immediate { Value: VTableOffset }] })
            return null;

        if (ChaseCopies(definitions, shifted) is not { OpCode: OpCode.ShiftLeft, Operands: [_, LocalVariable index, Immediate { Value: InvokeDataShift }] })
            return null;

        var entryOffset = ChaseCopies(definitions, index);

        if (entryOffset is { OpCode: OpCode.Add, Operands: [_, LocalVariable beforeSlot, Immediate slotAddend] })
        {
            if (slotAddend.Value != slot)
                return null;

            entryOffset = ChaseCopies(definitions, beforeSlot);
        }
        else if (slot != 0)
            return null;

        if (entryOffset is not { OpCode: OpCode.Move, Operands: [_, MemoryOperand { Base: not null }] })
            return null;

        return klassCandidate;
    }

    private static Instruction? Definition(Dictionary<LocalVariable, Instruction> definitions, LocalVariable local)
        => definitions.TryGetValue(local, out var definition) ? definition : null;

    private static Instruction? ChaseCopies(Dictionary<LocalVariable, Instruction> definitions, LocalVariable local)
    {
        var visited = new HashSet<LocalVariable>();

        while (visited.Add(local))
        {
            if (Definition(definitions, local) is not { } definition)
                return null;

            if (definition is { OpCode: OpCode.Move, Operands: [_, LocalVariable source] })
            {
                local = source;
                continue;
            }

            return definition;
        }

        return null;
    }

    private static MethodAnalysisContext? ResolveInterfaceSlot(TypeAnalysisContext declaringInterface, int slot)
    {
        if (declaringInterface is GenericInstanceTypeAnalysisContext genericInstance)
        {
            var baseMethod = genericInstance.GenericType.Methods.FirstOrDefault(m => m.Definition?.slot == slot);
            return baseMethod == null ? null : new ConcreteGenericMethodAnalysisContext(baseMethod, genericInstance.GenericArguments, []);
        }

        return declaringInterface.Methods.FirstOrDefault(m => m.Definition?.slot == slot);
    }

    private static void RewriteDispatch(MethodAnalysisContext method, Instruction dispatch, Block block, Match match, Dictionary<LocalVariable, Instruction> definitions)
    {
        var resolved = match.Resolved;
        var callingConventions = resolved.AppContext.InstructionSet.CallingConventionResolver;
        var isTailCall = dispatch.OpCode == OpCode.IndirectJump;

        // an IndirectJump's return register operand is a stale use rather than a return slot, so rebuild from scratch
        if (isTailCall)
        {
            var operands = new List<IOperand> { resolved };

            if (!resolved.IsVoid)
                operands.Add(new LocalVariable("interfaceTailCallResult", callingConventions?.ReturnRegister(resolved) ?? new Register(null, "rax")));

            operands.AddRange(dispatch.Operands.Skip(2));
            dispatch.SetOperands(operands);
        }
        else
        {
            if (resolved.IsVoid)
                dispatch.RemoveOperandAt(1);

            dispatch.SetOperand(0, resolved);
        }

        dispatch.OpCode = resolved.IsVoid ? OpCode.CallVoid : OpCode.Call;
        callingConventions?.RemapRawArguments(dispatch, resolved);

        // name [phi+8] as the hidden MethodInfo param, like ResolveVirtualCalls. A tail call's target
        // register doubles as an argument slot, so a stale [phi] load can turn up as an argument too,
        // and gets a placeholder so the VirtualInvokeData pointer still dies.
        var assembly = resolved.DeclaringType?.DeclaringAssembly ?? method.DeclaringType?.DeclaringAssembly;
        for (var i = 1; i < dispatch.Operands.Count; i++)
        {
            if (dispatch.Operands[i] is not LocalVariable argument
                || Definition(definitions, argument) is not { OpCode: OpCode.Move, Operands: [_, MemoryOperand { Index: null, Scale: 0, Base: LocalVariable loadBase } load] }
                || !ReferenceEquals(Definition(definitions, loadBase), match.InvokeDataPhi))
                continue;

            if (load.Addend == 8 && assembly != null)
                dispatch.SetOperand(i, new RuntimeMethodInfoAnalysisContext(resolved, assembly));
            else if (load.Addend == 0)
                dispatch.SetOperand(i, new Immediate(0));
        }

        if (isTailCall)
        {
            var returnOperands = !method.IsVoid && !resolved.IsVoid
                ? new List<IOperand> { dispatch.Operands[1] }
                : [];

            block.AddInstruction(new Instruction(-1, OpCode.Return, returnOperands));
            block.CalculateBlockType();
        }
    }

    // Bailing here is fine, it just leaves the (already resolved) call with dead lookup around it
    private static void TryExciseLookup(ISILControlFlowGraph cfg, Match match, Dictionary<LocalVariable, Instruction> definitions, Dictionary<Instruction, Block> homeBlock)
    {
        var merge = match.Merge;

        if (!homeBlock.TryGetValue(match.SlowCall, out var slowBlock))
            return;

        if (Definition(definitions, match.KlassLocal) is not { } klassDefinition
            || !homeBlock.TryGetValue(klassDefinition, out var head) || head == merge)
            return;

        if (!TryCollectRegion(cfg, head, merge, out var region) || !region.Contains(slowBlock))
            return;

        if (!RegionIsSideEffectFree(region, match.SlowCall) || AnyValueEscapes(cfg, region, merge))
            return;

        if (!MergePhisAreDead(cfg, merge, out var removable))
            return;

        foreach (var instruction in removable)
        {
            instruction.OpCode = OpCode.Nop;
            instruction.SetOperands();
        }

        foreach (var successor in head.Successors)
            successor.Predecessors.Remove(head);
        head.Successors.Clear();
        head.Successors.Add(merge);

        var terminator = head.Instructions[^1];
        if (terminator.OpCode is OpCode.Jump or OpCode.ConditionalJump)
        {
            terminator.OpCode = OpCode.Jump;
            terminator.SetOperands(merge);
        }
        else
            head.AddInstruction(new Instruction(-1, OpCode.Jump, merge));

        head.CalculateBlockType();

        merge.Predecessors.RemoveAll(region.Contains);
        merge.Predecessors.Add(head);

        foreach (var block in region)
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

    // The region has to be closed, so nothing else may enter or leave it
    private static bool TryCollectRegion(ISILControlFlowGraph cfg, Block head, Block merge, out HashSet<Block> region)
    {
        region = [];

        var queue = new Queue<Block>(merge.Predecessors);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();

            if (block == head)
                continue;

            if (block == merge || block == cfg.EntryBlock || block == cfg.ExitBlock || region.Count > 64)
                return false;

            if (!region.Add(block))
                continue;

            foreach (var predecessor in block.Predecessors)
                queue.Enqueue(predecessor);
        }

        if (region.Count == 0)
            return false;

        var collected = region;
        foreach (var block in collected)
        {
            if (block.Predecessors.Any(p => p != head && !collected.Contains(p)))
                return false;
            if (block.Successors.Any(s => s != merge && !collected.Contains(s)))
                return false;
        }

        // we rewrite the head's terminator, so it can't branch anywhere else
        return head.Successors.All(s => s == merge || collected.Contains(s));
    }

    private static bool RegionIsSideEffectFree(HashSet<Block> region, Instruction slowCall)
    {
        foreach (var block in region)
        {
            foreach (var instruction in block.Instructions)
            {
                if (ReferenceEquals(instruction, slowCall))
                    continue;

                var harmless = instruction.OpCode switch
                {
                    OpCode.Nop or OpCode.Jump or OpCode.ConditionalJump or OpCode.Phi => true,
                    OpCode.Move or OpCode.Add or OpCode.Subtract or OpCode.Multiply or OpCode.Divide or OpCode.Modulo
                        or OpCode.ShiftLeft or OpCode.ShiftRight or OpCode.And or OpCode.Or or OpCode.Xor
                        or OpCode.Not or OpCode.Negate
                        or (>= OpCode.CheckEqual and <= OpCode.CheckLessOrEqual)
                        => instruction.Destination is LocalVariable,
                    _ => false,
                };

                if (!harmless)
                    return false;
            }
        }

        return true;
    }

    // Merge phis are exempt, their deadness gets checked separately
    private static bool AnyValueEscapes(ISILControlFlowGraph cfg, HashSet<Block> region, Block merge)
    {
        var regionDefs = new HashSet<LocalVariable>();
        foreach (var block in region)
            foreach (var instruction in block.Instructions)
                if (instruction.Destination is LocalVariable destination)
                    regionDefs.Add(destination);

        foreach (var block in cfg.Blocks)
        {
            if (region.Contains(block))
                continue;

            foreach (var instruction in block.Instructions)
            {
                if (instruction.OpCode == OpCode.Phi && block == merge)
                    continue;

                if (Uses(instruction, regionDefs))
                    return true;
            }
        }

        return false;
    }

    // They may only feed loads off the VirtualInvokeData pointer, which must themselves be dead
    private static bool MergePhisAreDead(ISILControlFlowGraph cfg, Block merge, out List<Instruction> removable)
    {
        removable = [];

        var useSites = new Dictionary<LocalVariable, List<Instruction>>();
        foreach (var block in cfg.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                foreach (var used in UsedLocals(instruction))
                {
                    if (!useSites.TryGetValue(used, out var sites))
                        useSites[used] = sites = [];
                    sites.Add(instruction);
                }
            }
        }

        foreach (var phi in merge.Instructions)
        {
            if (phi.OpCode != OpCode.Phi)
                continue;

            if (phi.Operands[0] is not LocalVariable phiDest)
                return false;

            foreach (var use in useSites.TryGetValue(phiDest, out var phiUses) ? phiUses : [])
            {
                if (use is not { OpCode: OpCode.Move, Operands: [LocalVariable loaded, MemoryOperand] }
                    || (useSites.TryGetValue(loaded, out var loadUses) && loadUses.Count > 0))
                    return false;

                removable.Add(use);
            }

            removable.Add(phi);
        }

        return true;
    }

    private static bool Uses(Instruction instruction, HashSet<LocalVariable> candidates)
        => UsedLocals(instruction).Any(candidates.Contains);

    private static IEnumerable<LocalVariable> UsedLocals(Instruction instruction)
    {
        for (var i = 0; i < instruction.Operands.Count; i++)
        {
            if (ReferenceEquals(instruction.Operands[i], instruction.Destination))
                continue;

            switch (instruction.Operands[i])
            {
                case LocalVariable local:
                    yield return local;
                    break;
                case AddressOf { Target: LocalVariable addressed }:
                    yield return addressed;
                    break;
                case MemoryOperand memory:
                    if (memory.Base is LocalVariable baseLocal)
                        yield return baseLocal;
                    if (memory.Index is LocalVariable indexLocal)
                        yield return indexLocal;
                    break;
            }
        }
    }
}
