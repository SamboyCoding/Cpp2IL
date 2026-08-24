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
    public Dictionary<Block, HashSet<Block>> Dominators = new();

    public DominatorInfo(ISILControlFlowGraph graph)
    {
        // The post dominators are not actually used by anything.

        CalculateDominators(graph);
        //CalculatePostDominators(graph);

        CalculateImmediateDominators(graph);
        //CalculateImmediatePostDominators(graph);

        CalculateDominanceFrontiers(graph);
        BuildDominanceTree();
    }

    public bool Dominates(Block a, Block b)
    {
        if (a == b)
            return true;

        if (Dominators.TryGetValue(b, out var bDominators) && Dominators.ContainsKey(a))
            return bDominators.Contains(a);

        return false;
    }

    private void BuildDominanceTree()
    {
        foreach (var block in ImmediateDominators.Keys)
        {
            var immediateDominator = ImmediateDominators[block];
            if (immediateDominator == null) continue;

            if (!DominanceTree.ContainsKey(immediateDominator))
                DominanceTree[immediateDominator] = [];

            DominanceTree[immediateDominator].Add(block);
        }
    }

    private void CalculateDominators(ISILControlFlowGraph graph)
    {
        Dominators.Clear();

        // Entry block dominates itself, all others are initialized with all blocks
        foreach (var block in graph.Blocks)
        {
            var dominators = new HashSet<Block>();

#if NET5_0_OR_GREATER
            dominators.EnsureCapacity(graph.Blocks.Count);
#endif

            if (block == graph.EntryBlock)
            {
                dominators.Add(block);
            }
            else
            {
                foreach (var graphBlock in graph.Blocks)
                    dominators.Add(graphBlock);
            }

            Dominators[block] = dominators;
        }

        var remaining = new Stack<Block>(graph.Blocks);
        var tempDoms = new HashSet<Block>();

#if NET5_0_OR_GREATER
        tempDoms.EnsureCapacity(graph.Blocks.Count);
#endif

        // Get dominators
        while (remaining.Count > 0)
        {
            var block = remaining.Pop();

            if (block == graph.EntryBlock)
                continue;

            tempDoms.Clear();

            if (block.Predecessors.Count != 0)
            {
                foreach (var predecessor in Dominators[block.Predecessors[0]])
                    tempDoms.Add(predecessor);

                for (var i = 1; i < block.Predecessors.Count; i++)
                    tempDoms.IntersectWith(Dominators[block.Predecessors[i]]);
            }

            tempDoms.Add(block);

            // Given that all dominators can be at most the intersection of their predecessor dominators,
            // there is no case in which an entirely new dominator gets added to a dominator set.
            // this means that we do not have to compare all dominators by value; rather,
            // we can just compare if the amount of dominators is the same, as the only way for that
            // to be possible is for the same dominators to be in both sets.

            if (tempDoms.Count != Dominators[block].Count)
            {
                (Dominators[block], tempDoms) = (tempDoms, Dominators[block]);

                foreach (var successor in block.Successors)
                {
                    remaining.Push(successor);
                }
            }
        }
    }

    private void CalculatePostDominators(ISILControlFlowGraph graph)
    {
        PostDominators.Clear();

        foreach (var block in graph.Blocks)
        {
            if (block == graph.ExitBlock)
                PostDominators[block] = [block];
            else
                PostDominators[block] = new HashSet<Block>(graph.Blocks);
        }

        var remaining = new Stack<Block>(((IEnumerable<Block>)graph.Blocks).Reverse());

        while (remaining.Count > 0)
        {
            var block = remaining.Pop();

            if (block.Successors.Count == 0 && block != graph.ExitBlock)
                continue;

            var tempPostDoms = block.Successors.Count == 0
                ? new HashSet<Block>()
                : new HashSet<Block>(PostDominators[block.Successors[0]]);

            for (var i = 1; i < block.Successors.Count; i++)
                tempPostDoms.IntersectWith(PostDominators[block.Successors[i]]);

            tempPostDoms.Add(block);

            if (!tempPostDoms.SetEquals(PostDominators[block]))
            {
                PostDominators[block] = tempPostDoms;
                foreach (var predecessor in block.Predecessors)
                    remaining.Push(predecessor);
            }
        }
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

                while (runner != ImmediateDominators[block] && runner != null)
                {
                    DominanceFrontier[runner].Add(block);
                    runner = ImmediateDominators[runner];
                }
            }
        }
    }

    private void CalculateImmediatePostDominators(ISILControlFlowGraph graph)
    {
        foreach (var block in graph.Blocks)
            ImmediatePostDominators[block] = ClosestStrictDominator(block, PostDominators);
    }

    private void CalculateImmediateDominators(ISILControlFlowGraph graph)
    {
        foreach (var block in graph.Blocks)
            ImmediateDominators[block] = ClosestStrictDominator(block, Dominators);
    }

    /// <summary>
    /// Returns the (post-)dominator of <paramref name="block"/> closest to it, i.e. its immediate
    /// (post-)dominator. The strict (post-)dominators of a block form a chain under the domination
    /// relation, so the closest one is simply the one with the largest (post-)dominator set.
    /// Returns null for the root (entry/exit) or any block with no strict (post-)dominators.
    /// </summary>
    private static Block? ClosestStrictDominator(Block block, Dictionary<Block, HashSet<Block>> dominators)
    {
        if (!dominators.TryGetValue(block, out var doms))
            return null;

        Block? closest = null;
        var closestCount = -1;

        foreach (var candidate in doms)
        {
            if (candidate == block)
                continue;

            var count = dominators[candidate].Count;
            if (count > closestCount)
            {
                closestCount = count;
                closest = candidate;
            }
        }

        return closest;
    }
}
