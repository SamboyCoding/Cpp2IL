namespace Cpp2IL.Decompiler.ControlFlow;

/// <summary>
/// Dominance info.
/// Taken from https://github.com/SamboyCoding/Cpp2IL/blob/development-gompo-ast/Cpp2IL.Core/Graphs/DominatorInfo.cs
/// </summary>
public class Dominance
{
    /// <summary>
    /// Dominance tree (block, immediate dominators).
    /// </summary>
    public Dictionary<Block, List<Block>> DominanceTree = new();

    /// <summary>
    /// The dominance frontier for each block.
    /// </summary>
    public Dictionary<Block, HashSet<Block>> DominanceFrontier = new();

    /// <summary>
    /// The immediate dominators of each block.
    /// </summary>
    public Dictionary<Block, Block?> ImmediateDominators = new();

    /// <summary>
    /// The immediate post dominators of each block.
    /// </summary>
    public Dictionary<Block, Block?> ImmediatePostDominators = new();

    /// <summary>
    /// The post dominators of each block.
    /// </summary>
    public Dictionary<Block, HashSet<Block>> PostDominators = new();

    /// <summary>
    /// The dominators of each block.
    /// </summary>
    public Dictionary<Block, HashSet<Block>> Dominators = new();

    /// <summary>
    /// Builds dominance info for a control flow graph.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <returns>The dominance info.</returns>
    public static Dominance Build(ControlFlowGraph graph)
    {
        var dominance = new Dominance();
        dominance.CalculateDominators(graph);
        dominance.CalculatePostDominators(graph);
        dominance.CalculateImmediateDominators(graph);
        dominance.CalculateImmediatePostDominators(graph);
        dominance.CalculateDominanceFrontiers(graph);
        dominance.BuildDominanceTree();
        return dominance;
    }

    /// <summary>
    /// Checks if block <paramref name="a"/> dominates block <paramref name="b"/>.
    /// </summary>
    /// <param name="a">The potential dominator block.</param>
    /// <param name="b">The block to check dominance for.</param>
    /// <returns>True if <paramref name="a"/> dominates <paramref name="b"/>.</returns>
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

    private void CalculateDominators(ControlFlowGraph graph)
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

    private void CalculatePostDominators(ControlFlowGraph graph)
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
                if (block == graph.ExitBlock)
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

    private void CalculateDominanceFrontiers(ControlFlowGraph graph)
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

    private void CalculateImmediatePostDominators(ControlFlowGraph graph)
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

    private void CalculateImmediateDominators(ControlFlowGraph graph)
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
