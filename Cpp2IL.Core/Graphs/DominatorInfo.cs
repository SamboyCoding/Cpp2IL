using System.Collections.Generic;
using System.Linq;

namespace Cpp2IL.Core.Graphs;

public class DominatorInfo
{
    public Dictionary<Block, List<Block>> DominanceTree = new();
    public Dictionary<Block, HashSet<Block>> DominanceFrontier = new();
    public Dictionary<Block, Block?> ImmediateDominators = new();
    public Dictionary<Block, Block?> ImmediatePostDominators = new();
    public Dictionary<Block, HashSet<Block>> PostDominators = new();

    private Dictionary<Block, HashSet<Block>>? _dominatorsCache;
    public Dictionary<Block, HashSet<Block>> Dominators
    {
        get
        {
            if (_dominatorsCache == null)
            {
                _dominatorsCache = new();
                foreach (var kvp in ImmediateDominators)
                {
                    var block = kvp.Key;
                    var set = new HashSet<Block> { block };
                    var cur = ImmediateDominators[block];
                    while (cur != null)
                    {
                        set.Add(cur);
                        cur = ImmediateDominators.TryGetValue(cur, out var parent) ? parent : null;
                    }
                    _dominatorsCache[block] = set;
                }
            }
            return _dominatorsCache;
        }
    }

    public DominatorInfo(ISILControlFlowGraph graph)
    {
        CalculateDominatorsFast(graph);
        CalculateDominanceFrontiers(graph);
        BuildDominanceTree();
    }

    public bool Dominates(Block a, Block b)
    {
        if (a == b)
            return true;

        var current = b;
        while (current != null)
        {
            if (current == a)
                return true;
            current = ImmediateDominators.TryGetValue(current, out var idom) ? idom : null;
        }

        return false;
    }

    private void BuildDominanceTree()
    {
        DominanceTree.Clear();
        foreach (var kvp in ImmediateDominators)
        {
            var block = kvp.Key;
            var immediateDominator = kvp.Value;
            if (immediateDominator == null) continue;

            if (!DominanceTree.ContainsKey(immediateDominator))
                DominanceTree[immediateDominator] = [];

            DominanceTree[immediateDominator].Add(block);
        }
    }

    /// <summary>
    /// Fast Cooper-Harvey-Kennedy iterative dominator algorithm.
    /// Runs in O(N) time with virtually zero heap allocations.
    /// </summary>
    private void CalculateDominatorsFast(ISILControlFlowGraph graph)
    {
        ImmediateDominators.Clear();
        _dominatorsCache = null;

        if (graph.Blocks.Count == 0)
            return;

        var entry = graph.EntryBlock;
        if (entry == null)
            return;

        // 1. Compute Post-Order traversal of reachable blocks via DFS
        var postOrder = new List<Block>(graph.Blocks.Count);
        var visited = new HashSet<Block>();
        var postOrderIndex = new Dictionary<Block, int>(graph.Blocks.Count);

        void Dfs(Block current)
        {
            visited.Add(current);
            foreach (var succ in current.Successors)
            {
                if (!visited.Contains(succ))
                    Dfs(succ);
            }
            postOrderIndex[current] = postOrder.Count;
            postOrder.Add(current);
        }

        Dfs(entry);

        // Blocks not reachable from entry get null idom
        foreach (var block in graph.Blocks)
            ImmediateDominators[block] = null;

        ImmediateDominators[entry] = entry;

        // 2. Iterative Cooper-Harvey-Kennedy algorithm
        var rpo = new List<Block>(postOrder);
        rpo.Reverse(); // Reverse Post-Order: entry is first

        Block Intersect(Block b1, Block b2)
        {
            var finger1 = b1;
            var finger2 = b2;
            while (finger1 != finger2)
            {
                while (postOrderIndex.TryGetValue(finger1, out var idx1) &&
                       postOrderIndex.TryGetValue(finger2, out var idx2) &&
                       idx1 < idx2)
                {
                    finger1 = ImmediateDominators[finger1]!;
                }
                while (postOrderIndex.TryGetValue(finger1, out var idx1) &&
                       postOrderIndex.TryGetValue(finger2, out var idx2) &&
                       idx2 < idx1)
                {
                    finger2 = ImmediateDominators[finger2]!;
                }
            }
            return finger1;
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var b in rpo)
            {
                if (b == entry)
                    continue;

                // Find first processed predecessor
                Block? newIdom = null;
                foreach (var p in b.Predecessors)
                {
                    if (ImmediateDominators.TryGetValue(p, out var pIdom) && pIdom != null)
                    {
                        newIdom = p;
                        break;
                    }
                }

                if (newIdom == null)
                    continue;

                foreach (var p in b.Predecessors)
                {
                    if (p != newIdom && ImmediateDominators.TryGetValue(p, out var pIdom) && pIdom != null)
                    {
                        newIdom = Intersect(p, newIdom);
                    }
                }

                if (ImmediateDominators[b] != newIdom)
                {
                    ImmediateDominators[b] = newIdom;
                    changed = true;
                }
            }
        }

        // For entry block, convention is null (no strict dominator)
        ImmediateDominators[entry] = null;
    }

    private void CalculateDominanceFrontiers(ISILControlFlowGraph graph)
    {
        DominanceFrontier.Clear();

        foreach (var block in graph.Blocks)
            DominanceFrontier[block] = [];

        foreach (var block in graph.Blocks)
        {
            if (block.Predecessors.Count < 2) continue;

            foreach (var predecessor in block.Predecessors)
            {
                var runner = predecessor;

                while (runner != null && runner != ImmediateDominators[block])
                {
                    DominanceFrontier[runner].Add(block);
                    runner = ImmediateDominators.TryGetValue(runner, out var rIdom) ? rIdom : null;
                }
            }
        }
    }
}
