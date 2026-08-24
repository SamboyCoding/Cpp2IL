using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

// Merge the copies left behind by SSA destruction.
public static class CopyCoalescer
{
    public static void Run(MethodAnalysisContext method) => Run(method.ControlFlowGraph!);

    public static void Run(ISILControlFlowGraph cfg)
    {
        var copies = FindSameSlotCopies(cfg);
        var escapedSlots = FindEscapedSlotGroups(cfg);

        if (copies.Count == 0 && escapedSlots.Count == 0)
            return;

        var candidates = new HashSet<LocalVariable>();
        foreach (var (destination, source, _) in copies)
        {
            candidates.Add(destination);
            candidates.Add(source);
        }
        foreach (var group in escapedSlots)
            candidates.UnionWith(group);

        var interference = BuildInterference(cfg, candidates);
        var groups = new DisjointSet(candidates);

        foreach (var group in escapedSlots)
        {
            for (var i = 1; i < group.Count; i++)
            {
                var a = groups.Find(group[0]);
                var b = groups.Find(group[i]);

                if (a == b || (a.Type != null && b.Type != null && !ReferenceEquals(a.Type, b.Type)))
                    continue;

                groups.Union(a, b);
            }
        }

        foreach (var (destination, source, _) in copies)
        {
            var a = groups.Find(destination);
            var b = groups.Find(source);

            // different types would need a cast at every use, so do this only when the types agree, or one side is null
            if (a.Type != null && b.Type != null && !ReferenceEquals(a.Type, b.Type))
                continue;

            if (a == b || Interferes(interference, groups, a, b))
                continue;

            groups.Union(a, b);
        }

        Rewrite(cfg, groups);
    }

    private static List<(LocalVariable Destination, LocalVariable Source, Instruction Instruction)> FindSameSlotCopies(ISILControlFlowGraph cfg)
    {
        var copies = new List<(LocalVariable, LocalVariable, Instruction)>();

        foreach (var instruction in cfg.Instructions)
        {
            if (instruction.OpCode == OpCode.Move
                && instruction.Operands[0] is LocalVariable destination
                && instruction.Operands[1] is LocalVariable source
                && !ReferenceEquals(destination, source)
                && destination.Register.Number == source.Register.Number)
                copies.Add((destination, source, instruction));
        }

        return copies;
    }

    private static List<List<LocalVariable>> FindEscapedSlotGroups(ISILControlFlowGraph cfg)
    {
        var escapedSlotNumbers = new HashSet<int>();
        foreach (var instruction in cfg.Instructions)
            foreach (var operand in instruction.Operands)
                if (operand is AddressOf { Target: LocalVariable addressed })
                    escapedSlotNumbers.Add(addressed.Register.Number);

        if (escapedSlotNumbers.Count == 0)
            return [];

        var bySlot = new Dictionary<int, List<LocalVariable>>();
        var seen = new HashSet<LocalVariable>();

        foreach (var instruction in cfg.Instructions)
        {
            var locals = Used(instruction);
            if (Defined(instruction) is { } defined)
                locals = locals.Append(defined);

            foreach (var local in locals)
                if (escapedSlotNumbers.Contains(local.Register.Number) && seen.Add(local))
                {
                    if (!bySlot.TryGetValue(local.Register.Number, out var versions))
                        bySlot[local.Register.Number] = versions = [];
                    versions.Add(local);
                }
        }

        return bySlot.Values.Where(versions => versions.Count > 1).ToList();
    }

    private static bool Interferes(Dictionary<LocalVariable, HashSet<LocalVariable>> interference, DisjointSet groups, LocalVariable a, LocalVariable b)
    {
        foreach (var member in groups.Members(a))
        {
            if (!interference.TryGetValue(member, out var edges))
                continue;

            foreach (var edge in edges)
                if (groups.Find(edge) == b)
                    return true;
        }

        return false;
    }

    
    // interference edges between the candidates.
    // whatever is alive where a local is assigned has to keep its own storage, because both values are wanted at once.
    // except: the source of a copy is exempt against its own destination, so after <c>a = b</c> the two agree, because that is, after all, the whole point.
    private static Dictionary<LocalVariable, HashSet<LocalVariable>> BuildInterference(ISILControlFlowGraph cfg, HashSet<LocalVariable> candidates)
    {
        var liveOut = ComputeBlockLiveOut(cfg);
        var interference = new Dictionary<LocalVariable, HashSet<LocalVariable>>();

        void Connect(LocalVariable a, LocalVariable b)
        {
            if (ReferenceEquals(a, b))
                return;

            if (!interference.TryGetValue(a, out var edges))
                interference[a] = edges = [];
            edges.Add(b);

            if (!interference.TryGetValue(b, out var otherEdges))
                interference[b] = otherEdges = [];
            otherEdges.Add(a);
        }

        foreach (var block in cfg.Blocks)
        {
            var live = new HashSet<LocalVariable>(liveOut[block]);

            for (var i = block.Instructions.Count - 1; i >= 0; i--)
            {
                var instruction = block.Instructions[i];
                var defined = Defined(instruction);

                if (defined != null && candidates.Contains(defined))
                {
                    var copySource = instruction.OpCode == OpCode.Move ? instruction.Operands[1] as LocalVariable : null;

                    foreach (var other in live)
                        if (candidates.Contains(other) && !ReferenceEquals(other, copySource))
                            Connect(defined, other);
                }

                if (defined != null)
                    live.Remove(defined);

                foreach (var used in Used(instruction))
                    live.Add(used);
            }
        }

        return interference;
    }

    private static Dictionary<Block, HashSet<LocalVariable>> ComputeBlockLiveOut(ISILControlFlowGraph cfg)
    {
        var liveIn = new Dictionary<Block, HashSet<LocalVariable>>();
        var liveOut = new Dictionary<Block, HashSet<LocalVariable>>();

        foreach (var block in cfg.Blocks)
        {
            liveIn[block] = [];
            liveOut[block] = [];
        }

        var remaining = new Stack<Block>(cfg.Blocks);

        while (remaining.Count > 0)
        {
            var block = remaining.Pop();

            var outSet = new HashSet<LocalVariable>();
            foreach (var successor in block.Successors)
                outSet.UnionWith(liveIn[successor]);

            var inSet = new HashSet<LocalVariable>(outSet);
            for (var i = block.Instructions.Count - 1; i >= 0; i--)
            {
                var instruction = block.Instructions[i];

                if (Defined(instruction) is { } defined)
                    inSet.Remove(defined);

                foreach (var used in Used(instruction))
                    inSet.Add(used);
            }

            if (!outSet.SetEquals(liveOut[block]) || !inSet.SetEquals(liveIn[block]))
            {
                liveOut[block] = outSet;
                liveIn[block] = inSet;
                
                foreach (var predecessor in block.Predecessors)
                    remaining.Push(predecessor);
            }
        }

        return liveOut;
    }

    private static LocalVariable? Defined(Instruction instruction) => instruction.Destination as LocalVariable;

    private static IEnumerable<LocalVariable> Used(Instruction instruction)
    {
        var defined = Defined(instruction);

        foreach (var operand in instruction.Operands)
        {
            switch (operand)
            {
                case LocalVariable local when !ReferenceEquals(local, defined):
                    yield return local;
                    break;
                case MemoryOperand memory:
                    if (memory.Base is LocalVariable memoryBase)
                        yield return memoryBase;
                    if (memory.Index is LocalVariable memoryIndex)
                        yield return memoryIndex;
                    break;
                case FieldReference field:
                    yield return field.Local;
                    break;
                case ArrayAccess array:
                    yield return array.Array;
                    if (array.Index is LocalVariable arrayIndex)
                        yield return arrayIndex;
                    break;
                case ArrayLength length:
                    yield return length.Array;
                    break;
                case AddressOf { Target: LocalVariable addressed }:
                    yield return addressed;
                    break;
            }
        }
    }

    private static void Rewrite(ISILControlFlowGraph cfg, DisjointSet groups)
    {
        foreach (var block in cfg.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                for (var i = 0; i < instruction.Operands.Count; i++)
                {
                    switch (instruction.Operands[i])
                    {
                        case LocalVariable local:
                            instruction.SetOperand(i, groups.Find(local));
                            break;
                        case MemoryOperand memory:
                            if (memory.Base is LocalVariable memoryBase)
                                memory.Base = groups.Find(memoryBase);
                            if (memory.Index is LocalVariable memoryIndex)
                                memory.Index = groups.Find(memoryIndex);
                            instruction.SetOperand(i, memory); // MemoryOperand is a struct, write the copy back
                            break;
                        case FieldReference field:
                            field.Local = groups.Find(field.Local);
                            break;
                        case ArrayAccess array:
                            array.Array = groups.Find(array.Array);
                            if (array.Index is LocalVariable arrayIndex)
                                array.Index = groups.Find(arrayIndex);
                            break;
                        case ArrayLength length:
                            length.Array = groups.Find(length.Array);
                            break;
                        case AddressOf { Target: LocalVariable addressed } addressOf:
                            addressOf.Target = groups.Find(addressed);
                            break;
                    }
                }
            }

            // the copies that have become self-assignments are why we did this, they're now noise
            foreach (var instruction in block.Instructions)
            {
                if (instruction.OpCode == OpCode.Move
                    && instruction.Operands[0] is LocalVariable destination
                    && instruction.Operands[1] is LocalVariable source
                    && ReferenceEquals(destination, source))
                {
                    instruction.OpCode = OpCode.Nop;
                    instruction.SetOperands();
                }
            }
        }
    }

    private class DisjointSet(IEnumerable<LocalVariable> locals)
    {
        private readonly Dictionary<LocalVariable, LocalVariable> _parent = locals.ToDictionary(l => l, l => l);
        private readonly Dictionary<LocalVariable, List<LocalVariable>> _members = locals.ToDictionary(l => l, l => new List<LocalVariable> { l });

        public LocalVariable Find(LocalVariable local)
        {
            if (!_parent.TryGetValue(local, out var parent))
                return local;

            while (!ReferenceEquals(parent, _parent[parent]))
                parent = _parent[parent];

            _parent[local] = parent;
            return parent;
        }

        public IEnumerable<LocalVariable> Members(LocalVariable representative) => _members[representative];

        public void Union(LocalVariable a, LocalVariable b)
        {
            // keep whichever already carries a type
            var (keep, drop) = a.Type != null || b.Type == null ? (a, b) : (b, a);

            _parent[drop] = keep;
            _members[keep].AddRange(_members[drop]);
            _members.Remove(drop);

            keep.Type ??= drop.Type;
            keep.IsThis |= drop.IsThis;
            keep.IsReturn |= drop.IsReturn;
            keep.IsMethodInfo |= drop.IsMethodInfo;
        }
    }
}
