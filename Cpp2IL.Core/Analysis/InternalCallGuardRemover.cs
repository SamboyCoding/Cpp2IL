using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

//Rewrites (i.e. removes) the "lazy-init with fallback to MissingMethodException" surrounding a InternalCalls_Resolve call
public static class InternalCallGuardRemover
{
    private const string Resolve = nameof(BaseKeyFunctionAddresses.InternalCalls_Resolve);

    public static void Run(MethodAnalysisContext method)
    {
        var cfg = method.ControlFlowGraph!;
        var removedAny = false;

        foreach (var block in cfg.Blocks.ToList())
        {
            var resolve = block.Instructions.FirstOrDefault(IsResolveCall);

            if (resolve != null)
                removedAny |= TryRemove(method, cfg, block, resolve);
        }

        if (BindCallsToResolvedPointers(cfg))
            removedAny = true;

        if (removedAny)
            DeadCodeEliminator.Run(cfg);
    }

    private static bool IsResolveCall(Instruction instruction) =>
        instruction is { OpCode: OpCode.Call, Operands: [StringLiteral { Value: Resolve }, _, _, ..] };

    private static ulong? NameAddress(ISILControlFlowGraph cfg, IOperand operand) => operand switch
    {
        Immediate immediate => immediate.UnsignedValue,
        LocalVariable local => cfg.Instructions
            .Where(i => i is { OpCode: OpCode.Move, Operands: [LocalVariable, Immediate] })
            .Where(i => ReferenceEquals(i.Operands[0], local))
            .Select(i => (ulong?)((Immediate)i.Operands[1]).UnsignedValue)
            .FirstOrDefault(),
        _ => null,
    };

    private static bool TryRemove(MethodAnalysisContext method, ISILControlFlowGraph cfg, Block resolveBlock, Instruction resolve)
    {
        if (NameAddress(cfg, resolve.Operands[2]) is not { } nameAddress)
            return false;

        var name = ThrowHelperRecovery.ReadCStringAtVirtualAddress(method.AppContext, nameAddress, 256);

        if (name == null || KeyFunctionRecovery.ResolveInternalCallName(method.AppContext, name) is not { DeclaringType.DeclaringAssembly: { } assembly } resolved)
            return false;

        var pointer = new RuntimeMethodInfoAnalysisContext(resolved, assembly);

        if (resolveBlock.Predecessors.Count != 1)
            return RewriteInPlace(cfg, resolve, pointer);

        var guard = resolveBlock.Predecessors[0];

        if (guard is not { BlockType: BlockType.TwoWay, Successors.Count: 2 }
            || guard.Instructions.Count == 0 || guard.Instructions[^1].OpCode != OpCode.ConditionalJump)
            return RewriteInPlace(cfg, resolve, pointer);

        var merge = guard.Successors[0] == resolveBlock ? guard.Successors[1] : guard.Successors[0];

        if (merge == resolveBlock || merge == cfg.EntryBlock || merge == cfg.ExitBlock)
            return RewriteInPlace(cfg, resolve, pointer);

        if (!TryCollectRegion(cfg, guard, resolveBlock, merge, out var region, out var cacheAddress))
            return RewriteInPlace(cfg, resolve, pointer);

        MetadataInitGuardRemover.Excise(cfg, guard, resolveBlock, merge, region);

        RewriteCacheReads(cfg, cacheAddress, pointer);
        return true;
    }

    private static bool TryCollectRegion(ISILControlFlowGraph cfg, Block guard, Block resolveBlock, Block merge, out HashSet<Block> region, out long cacheAddress)
    {
        region = [];
        cacheAddress = 0;

        var queue = new Queue<Block>();
        queue.Enqueue(resolveBlock);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();

            // Reaching the merge or falling off the end of the method are both fine, they just aren't ours
            if (block == merge || block == cfg.ExitBlock)
                continue;

            if (block == cfg.EntryBlock || block == guard)
                return false;

            if (!region.Add(block))
                continue;

            foreach (var instruction in block.Instructions)
                if (instruction is { OpCode: OpCode.Move, Operands: [MemoryOperand { IsConstant: true } cache, _] })
                    cacheAddress = cache.Addend;

            foreach (var successor in block.Successors)
                queue.Enqueue(successor);
        }

        // Without the write back to the cache this isn't the resolution region
        return cacheAddress != 0;
    }

    private static void RewriteCacheReads(ISILControlFlowGraph cfg, long cacheAddress, RuntimeMethodInfoAnalysisContext pointer)
    {
        foreach (var instruction in cfg.Instructions)
            if (instruction is { OpCode: OpCode.Move, Operands: [LocalVariable destination, MemoryOperand { IsConstant: true } cache] }
                && cache.Addend == cacheAddress)
                instruction.SetOperands(destination, pointer);
    }

    private static bool RewriteInPlace(ISILControlFlowGraph cfg, Instruction resolve, RuntimeMethodInfoAnalysisContext pointer)
    {
        resolve.OpCode = OpCode.Move;
        resolve.SetOperands(resolve.Operands[1], pointer);
        return true;
    }

    private static bool BindCallsToResolvedPointers(ISILControlFlowGraph cfg)
    {
        var pointers = new Dictionary<LocalVariable, MethodAnalysisContext>();

        bool Learn()
        {
            var learned = false;

            foreach (var instruction in cfg.Instructions)
            {
                if (instruction.Destination is not LocalVariable destination || pointers.ContainsKey(destination))
                    continue;

                var source = instruction switch
                {
                    { OpCode: OpCode.Move, Operands: [_, RuntimeMethodInfoAnalysisContext { RepresentedMethod: { } represented }] } => represented,
                    { OpCode: OpCode.Move or OpCode.Phi, Operands: [_, LocalVariable copied] } when pointers.TryGetValue(copied, out var known) => known,
                    _ => null,
                };

                if (source == null)
                    continue;

                pointers[destination] = source;
                learned = true;
            }

            return learned;
        }

        while (Learn()) { }

        var changed = false;

        foreach (var instruction in cfg.Instructions)
        {
            if (instruction.OpCode != OpCode.IndirectCall)
                continue;

            var resolved = instruction.Operands[0] switch
            {
                LocalVariable target when pointers.TryGetValue(target, out var known) => known,
                RuntimeMethodInfoAnalysisContext { RepresentedMethod: { } direct } => direct,
                _ => null,
            };

            if (resolved == null)
                continue;

            instruction.OpCode = OpCode.Call; // same operand layout, and the target is known now
            instruction.SetOperand(0, resolved);
            resolved.AppContext.InstructionSet.CallingConventionResolver?.RemapRawArguments(instruction, resolved);
            changed = true;
        }

        return changed;
    }
}
