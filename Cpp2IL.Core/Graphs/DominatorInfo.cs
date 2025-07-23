using System.Collections.Generic;

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
        CalculateDominators(graph);
        CalculatePostDominators(graph);
        CalculateImmediateDominators(graph);
        CalculateImmediatePostDominators(graph);
        CalculateDominanceFrontiers(graph);
        BuildDominanceTree();
    }

    public bool Dominates(Block a, Block b)
    {
        if (a == b)
            return true;
        if (Dominators.ContainsKey(b) && Dominators.ContainsKey(a))
            return Dominators[b].Contains(a);
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
            if (block == graph.EntryBlock)
                Dominators[block] = [block];
            else
                Dominators[block] = new HashSet<Block>(graph.Blocks);
        }

        var changed = true;

        // Get dominators
        while (changed)
        {
            changed = false;

            foreach (var block in graph.Blocks)
            {
                if (block == graph.EntryBlock)
                    continue;

                var tempDoms = block.Predecessors.Count == 0
                    ? new HashSet<Block>()
                    : new HashSet<Block>(Dominators[block.Predecessors[0]]);

                for (var i = 1; i < block.Predecessors.Count; i++)
                    tempDoms.IntersectWith(Dominators[block.Predecessors[i]]);

                tempDoms.Add(block);

                if (!tempDoms.SetEquals(Dominators[block]))
                {
                    Dominators[block] = tempDoms;
                    changed = true;
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

        var changed = true;

        while (changed)
        {
            changed = false;

            foreach (var block in graph.Blocks)
            {
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
                    changed = true;
                }
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
        {
            if (block.Successors.Count == 0 || block == graph.ExitBlock)
            {
                ImmediatePostDominators[block] = null;
                continue;
            }

            foreach (var candidate in PostDominators[block])
            {
                if (candidate == block)
                    continue;

                if (PostDominators[block].Count == 2)
                {
                    ImmediatePostDominators[block] = candidate;
                    break;
                }

                foreach (var otherCandidate in PostDominators[block])
                {
                    if (candidate == otherCandidate || candidate == block)
                        continue;

                    if (!PostDominators[otherCandidate].Contains(candidate))
                    {
                        ImmediatePostDominators[block] = candidate;
                        break;
                    }
                }
            }
        }
    }

    private void CalculateImmediateDominators(ISILControlFlowGraph graph)
    {
        foreach (var block in graph.Blocks)
            ImmediateDominators[block] = null;

        foreach (var block in graph.Blocks)
        {
            if (block.Predecessors.Count == 0 || block == graph.EntryBlock)
                continue;

            foreach (var candidate in Dominators[block])
            {
                if (candidate == block)
                    continue;

                if (Dominators[block].Count == 2)
                {
                    ImmediateDominators[block] = candidate;
                    break;
                }

                foreach (var otherCandidate in Dominators[block])
                {
                    if (candidate == otherCandidate || candidate == block)
                        continue;

                    if (!Dominators[otherCandidate].Contains(candidate))
                    {
                        ImmediateDominators[block] = candidate;
                        break;
                    }
                }
            }
        }
    }
}
