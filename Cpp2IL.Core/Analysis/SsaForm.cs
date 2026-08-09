using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Converts the control flow graph into and out of minimal SSA form, following the standard
/// Cytron et al. algorithm: phi functions are inserted at the iterated dominance frontiers of
/// each variable's definition sites, and the registers are then renamed (versioned) via a
/// pre-order walk of the dominator tree.
///
/// Variables are <see cref="Register"/>s, identified by <see cref="Register.Number"/>. Version
/// -1 represents the value on entry to the method (parameters / live-in values); real
/// definitions are numbered from 1 upwards.
/// </summary>
public class SsaForm
{
    // Per-register version stack (top = current version in the current dominator-tree path).
    private readonly Dictionary<int, Stack<Register>> _stacks = new();
    // Per-register last-assigned version number.
    private readonly Dictionary<int, int> _counter = new();
    // An unversioned representative register per number, used to build phi nodes and the entry value.
    private readonly Dictionary<int, Register> _repr = new();

    public static void Build(MethodAnalysisContext method)
        => Build(method.ControlFlowGraph!, method.DominatorInfo!);

    public static void Build(ISILControlFlowGraph graph, DominatorInfo dominatorInfo)
    {
        var ssa = new SsaForm();
        ssa.FindClobberingAddressTakes(graph);

        graph.BuildUseDefLists(ssa._clobbering);

        ssa.CollectRegisters(graph);
        ssa.InsertPhiFunctions(graph, dominatorInfo);
        ssa.Rename(graph.EntryBlock, dominatorInfo);
    }

    // The address-takes whose slot is read again afterwards, and so have to be treated as definitions.
    private readonly HashSet<Instruction> _clobbering = [];

    private void FindClobberingAddressTakes(ISILControlFlowGraph graph)
    {
        foreach (var block in graph.Blocks)
        {
            for (var i = 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];

                foreach (var operand in instruction.Operands)
                {
                    if (operand is AddressOf { Target: Register addressed } && IsReadAfter(block, i, addressed))
                        _clobbering.Add(instruction);
                }
            }
        }
    }

    private static bool IsReadAfter(Block block, int index, Register register)
    {
        if (ScanForRead(block, index + 1, register, out var continuePastBlock))
            return true;

        if (!continuePastBlock)
            return false;

        var visited = new HashSet<Block>();
        var queue = new Queue<Block>(block.Successors);

        while (queue.Count > 0)
        {
            var reachable = queue.Dequeue();

            if (!visited.Add(reachable))
                continue;

            if (ScanForRead(reachable, 0, register, out var keepGoing))
                return true;

            if (!keepGoing)
                continue;

            foreach (var successor in reachable.Successors)
                queue.Enqueue(successor);
        }

        return false;
    }

    // Scans a block from an index. Reports whether the register is read, and whether the paths beyond
    // this block are still worth following (they aren't once something has reassigned it).
    private static bool ScanForRead(Block block, int from, Register register, out bool continuePastBlock)
    {
        continuePastBlock = true;

        for (var i = from; i < block.Instructions.Count; i++)
        {
            var instruction = block.Instructions[i];

            if (Reads(instruction, register))
                return true;

            if (instruction.Destination is Register defined && defined.Number == register.Number)
            {
                continuePastBlock = false;
                return false;
            }
        }

        return false;
    }

    // A plain read of the register's value. The address-takes themselves don't count.
    private static bool Reads(Instruction instruction, Register register)
    {
        for (var i = 0; i < instruction.Operands.Count; i++)
        {
            if (i == 0 && instruction.Destination is Register)
                continue;

            var reads = instruction.Operands[i] switch
            {
                Register other => other.Number == register.Number,
                MemoryOperand memory => (memory.Base as Register?)?.Number == register.Number
                    || (memory.Index as Register?)?.Number == register.Number,
                _ => false
            };

            if (reads)
                return true;
        }

        return false;
    }

    private void CollectRegisters(ISILControlFlowGraph graph)
    {
        foreach (var instruction in graph.Instructions)
            foreach (var register in EnumerateRegisters(instruction))
                if (!_repr.ContainsKey(register.Number))
                    _repr[register.Number] = register.Copy();
    }

    private static IEnumerable<Register> EnumerateRegisters(Instruction instruction)
    {
        if (instruction.ImplicitDefinition is { } clobbered)
            yield return clobbered;

        foreach (var operand in instruction.Operands)
        {
            if (operand is Register register)
                yield return register;
            else if (operand is AddressOf { Target: Register addressed })
                yield return addressed;
            else if (operand is MemoryOperand memory)
            {
                if (memory.Base is Register baseRegister)
                    yield return baseRegister;
                if (memory.Index is Register indexRegister)
                    yield return indexRegister;
            }
        }
    }

    private void InsertPhiFunctions(ISILControlFlowGraph graph, DominatorInfo dominance)
    {
        var defSites = GetDefinitionSites(graph);

        foreach (var entry in defSites)
        {
            var regNumber = entry.Key;
            var sites = entry.Value;

            var workList = new Queue<Block>(sites);
            var onWorkList = new HashSet<Block>(sites);
            var hasPhi = new HashSet<Block>();

            while (workList.Count > 0)
            {
                var block = workList.Dequeue();

                if (!dominance.DominanceFrontier.TryGetValue(block, out var frontier))
                    continue;

                foreach (var frontierBlock in frontier)
                {
                    // Only one phi per (block, register).
                    if (!hasPhi.Add(frontierBlock))
                        continue;

                    InsertPhiSkeleton(frontierBlock, regNumber);

                    // Inserting a phi is itself a definition, so propagate to its frontier too.
                    if (onWorkList.Add(frontierBlock))
                        workList.Enqueue(frontierBlock);
                }
            }
        }
    }

    private static Dictionary<int, HashSet<Block>> GetDefinitionSites(ISILControlFlowGraph graph)
    {
        var defSites = new Dictionary<int, HashSet<Block>>();

        foreach (var block in graph.Blocks)
        {
            foreach (var operand in block.Def)
            {
                if (operand is not Register register)
                    continue;

                if (!defSites.TryGetValue(register.Number, out var sites))
                    defSites[register.Number] = sites = [];

                sites.Add(block);
            }
        }

        return defSites;
    }

    /// <summary>
    /// Inserts an unresolved phi node at the top of <paramref name="block"/> with one source slot
    /// per predecessor (positionally aligned to <see cref="Block.Predecessors"/>). The destination
    /// and source placeholders are versioned later during renaming.
    /// </summary>
    private void InsertPhiSkeleton(Block block, int regNumber)
    {
        var register = _repr[regNumber];

        var operands = new List<IOperand>(1 + block.Predecessors.Count) { register }; // destination first
        for (var i = 0; i < block.Predecessors.Count; i++)
            operands.Add(register); // one source per predecessor, filled in during renaming

        block.Instructions.Insert(0, new Instruction(-1, OpCode.Phi, operands));
    }

    private void Rename(Block initialBlock, DominatorInfo dominance)
    {
        var remaining = new Stack<(Stack<Block>, List<int>)>();
        remaining.Push((new Stack<Block>([initialBlock]), []));

        while (remaining.Count > 0)
        {
            var (blocks, parentDefinedRegisters) = remaining.Pop();
            if (blocks.Count == 0)
            {
                // Leaving the block: pop the versions it defined.
                foreach (var regNumber in parentDefinedRegisters)
                    _stacks[regNumber].Pop();

                continue;
            }

            var block = blocks.Pop();
            remaining.Push((blocks, parentDefinedRegisters));

            // Register numbers newly defined in this block, so we can pop their versions on the way out.
            var definedHere = new List<int>();

            foreach (var instruction in block.Instructions)
            {
                // A phi's operands belong to the incoming edges, so they are filled by predecessors;
                // only its destination is renamed here.
                if (instruction.OpCode != OpCode.Phi)
                    RewriteUses(instruction);

                if (instruction.Destination is Register definition)
                    instruction.Destination = NewName(definition, definedHere);

                // Nothing to write the new version back into, but taking it off the stack is the point: reads
                // after this one can't reach back past the call
                if (instruction.ImplicitDefinition is { } clobbered)
                    instruction.ImplicitDefinition = NewName(clobbered, definedHere);

                for (var i = 0; i < instruction.Operands.Count; i++)
                {
                    // Taking a slot's address lets the callee assign it, so the slot stops holding anything that reached this point, UNLESS
                    // nothing reads it afterwards, in which case any write is unobservable and the callee is only reading the value it has now
                    if (instruction.Operands[i] is AddressOf { Target: Register addressed })
                        instruction.SetOperand(i, new AddressOf(_clobbering.Contains(instruction)
                            ? NewName(addressed, definedHere)
                            : CurrentVersion(addressed.Number)));
                }
            }

            // Resolve the phi operands of successors that correspond to this block's outgoing edge.
            foreach (var successor in block.Successors)
            {
                var predIndex = successor.Predecessors.IndexOf(block);
                if (predIndex < 0)
                    continue;

                foreach (var phi in successor.Instructions)
                {
                    if (phi.OpCode != OpCode.Phi)
                        continue;

                    var regNumber = ((Register)phi.Operands[0]).Number;
                    phi.SetOperand(1 + predIndex, CurrentVersion(regNumber));
                }
            }

            // Recurse over the dominator tree.
            dominance.DominanceTree.TryGetValue(block, out var children);
            remaining.Push((new Stack<Block>(children ?? []), definedHere));
        }
    }

    private void RewriteUses(Instruction instruction)
    {
        for (var i = 0; i < instruction.Operands.Count; i++)
        {
            var operand = instruction.Operands[i];

            if (operand is Register register)
            {
                instruction.SetOperand(i, CurrentVersion(register.Number));
            }
            else if (operand is MemoryOperand memory)
            {
                if (memory.Base is Register baseRegister)
                    memory.Base = CurrentVersion(baseRegister.Number);
                if (memory.Index is Register indexRegister)
                    memory.Index = CurrentVersion(indexRegister.Number);

                instruction.SetOperand(i, memory); // MemoryOperand is a struct, write the copy back
            }
        }
    }

    /// <summary>
    /// The version of <paramref name="regNumber"/> currently in scope, or the entry value
    /// (version -1) if it has not been defined on the current path.
    /// </summary>
    private Register CurrentVersion(int regNumber)
    {
        if (_stacks.TryGetValue(regNumber, out var stack) && stack.Count > 0)
            return stack.Peek();

        return _repr.TryGetValue(regNumber, out var register) ? register : new Register(regNumber, null);
    }

    private Register NewName(Register register, List<int> definedHere)
    {
        var regNumber = register.Number;

        var version = _counter.TryGetValue(regNumber, out var current) ? current + 1 : 1;
        _counter[regNumber] = version;

        var versioned = register.Copy(version);

        if (!_stacks.TryGetValue(regNumber, out var stack))
            _stacks[regNumber] = stack = new Stack<Register>();

        stack.Push(versioned);
        definedHere.Add(regNumber);

        return versioned;
    }

    /// <summary>
    /// Destroys SSA form by replacing each phi with copies on the incoming edges. For a phi
    /// <c>dest = phi(s0, s1, ...)</c> a <c>Move dest, s[i]</c> is appended (before the terminator)
    /// to the i-th predecessor. Phi operands are positionally aligned to the predecessor list, so
    /// the i-th source belongs to the i-th predecessor.
    /// </summary>
    public static void Remove(MethodAnalysisContext method)
    {
        var cfg = method.ControlFlowGraph!;

        foreach (var block in cfg.Blocks)
        {
            var phiInstructions = block.Instructions
                .Where(i => i.OpCode == OpCode.Phi)
                .ToList();

            if (phiInstructions.Count == 0)
                continue;

            for (var predIndex = 0; predIndex < block.Predecessors.Count; predIndex++)
            {
                var predecessor = block.Predecessors[predIndex];
                var moves = new List<Instruction>();

                foreach (var phi in phiInstructions)
                {
                    if (1 + predIndex >= phi.Operands.Count)
                        continue;

                    var destination = phi.Operands[0];
                    var source = phi.Operands[1 + predIndex];

                    // Skip redundant self-copies.
                    if (Equals(destination, source))
                        continue;

                    moves.Add(new Instruction(-1, OpCode.Move, destination, source));
                }

                InsertBeforeTerminator(predecessor, moves);
            }

            foreach (var phi in phiInstructions)
            {
                phi.OpCode = OpCode.Nop;
                phi.SetOperands();
            }
        }

        cfg.RemoveNops();
        cfg.RemoveEmptyBlocks();
    }

    /// <summary>
    /// Inserts <paramref name="moves"/> at the end of <paramref name="block"/>, but before any
    /// trailing control-flow instruction, so the copies execute on the outgoing edge.
    /// </summary>
    private static void InsertBeforeTerminator(Block block, List<Instruction> moves)
    {
        if (moves.Count == 0)
            return;

        var insertAt = block.Instructions.Count;

        if (insertAt > 0 && !block.Instructions[insertAt - 1].IsFallThrough)
            insertAt--;

        block.Instructions.InsertRange(insertAt, moves);
    }
}
