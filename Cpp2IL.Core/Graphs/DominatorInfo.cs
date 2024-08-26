using System.Collections.Generic;

namespace Cpp2IL.Core.Graphs;

public sealed class DominatorInfo<T> where T : notnull
{ 
    private Dictionary<Block<T>, HashSet<Block<T>>> domFrontier = new();
    private Dictionary<Block<T>, Block<T>?> idom = new();
    private Dictionary<Block<T>, Block<T>?> iPostDom = new();
    private Dictionary<Block<T>, HashSet<Block<T>>> pDominators = new();
    private Dictionary<Block<T>, HashSet<Block<T>>> dominators = new();
    private DominatorInfo()
    {
    }

    public static DominatorInfo<T> From(ControlFlowGraph<T> graph)
    {
        var dominatorInfo = new DominatorInfo<T>();

        dominatorInfo.CalculateDominators(graph);
        dominatorInfo.CalculatePostDominators(graph);
        dominatorInfo.CalculateImmediateDominators(graph);
        dominatorInfo.CalculateImmediatePostDominators(graph);
        dominatorInfo.CalculateDominanceFrontiers(graph);

        return dominatorInfo;
    }

    public bool Dominates(Block<T> a, Block<T> b)
    {
        if (a == b)
            return true;
        if (dominators.ContainsKey(b) && dominators.ContainsKey(a))
            return dominators[b].Contains(a);
        return false;
    }

    // TODO: Implement api & tests


    private void CalculateDominanceFrontiers(ControlFlowGraph<T> graph)
    {
        // The dominance frontier of a basic block N is the set of all blocks that are
        // immediate successors to blocks dominated by N, but which aren’t themselves
        // strictly dominated by N. In other words, it represents the blocks that
        // are “first reached” on paths from N.
        domFrontier.Clear();
        foreach (var block in graph.Blocks)
        {
            domFrontier[block] = new();
        }
        
        foreach (var block in graph.Blocks)
        {
            if (block.Predecessors.Count >= 2)
            {
                foreach (var predecessor in block.Predecessors)
                {
                    var runner = predecessor;
                    while (runner != idom[block] && runner != null)
                    {
                        domFrontier[runner].Add(block);
                        runner = idom[runner];
                    }
                }
            }
        }
    }

    private void CalculateImmediatePostDominators(ControlFlowGraph<T> graph)
    {
        foreach (var block in graph.Blocks)
        {
            // TODO: Technically the exit block should be the only block with no successors
            // Requires switch & try/catch blocks to be properly handled
            if (block.Successors.Count == 0 || block.BlockType == BlockType.Exit)
            {
                iPostDom[block] = null;
                continue;
            }

            foreach (var candidate in pDominators[block])
            {
                if (candidate == block)
                    continue;

                if (pDominators[block].Count == 2)
                {
                    iPostDom[block] = candidate;
                    break;
                }

                foreach (var otherCandiate in pDominators[block])
                {
                    if (candidate == otherCandiate || candidate == block)
                        continue;

                    if (!pDominators[otherCandiate].Contains(candidate))
                    {
                        iPostDom[block] = candidate;
                        break;
                    }
                }
            }
        }
    }

    private void CalculateImmediateDominators(ControlFlowGraph<T> graph)
    {
        foreach (var block in graph.Blocks)
        {
            // TODO: Technically the exit block should be the only block with no successors
            // Requires switch & try/catch blocks to be properly handled
            if (block.Predecessors.Count == 0 || block.BlockType == BlockType.Entry)
            {
                idom[block] = null;
                continue;
            }

            // The idom of a node n is the unique node in Dom(n) that strictly dominates n
            // but does not strictly dominate any other node that strictly dominates n
            foreach (var candidate in dominators[block])
            {
                if (candidate == block)
                    continue;

                if (dominators[block].Count == 2)
                {
                    idom[block] = candidate;
                    break;
                }

                foreach (var otherCandiate in dominators[block])
                {
                    if (candidate == otherCandiate || candidate == block)
                        continue;

                    if (!dominators[otherCandiate].Contains(candidate))
                    {
                        idom[block] = candidate;
                        break;
                    }
                }
            }
        }
    }

    private void CalculatePostDominators(ControlFlowGraph<T> graph)
    {
        pDominators.Clear();
        foreach (var block in graph.Blocks)
        {
            
            if (block.BlockType == BlockType.Exit)
            {
                pDominators[block] = new();
                pDominators[block].Add(block);
            }
            else
            {
                pDominators[block] = new HashSet<Block<T>>(graph.Blocks);
            }
        }


        bool changed = true;

        while (changed)
        {
            changed = false;

            foreach (var block in graph.Blocks)
            {
                if (block.BlockType == BlockType.Exit)
                    continue;


                // if (block.Successors.Count == 0)
                // {
                //      return;
                // }

                var tmpPDominators = block.Successors.Count == 0 ? new HashSet<Block<T>>() : new HashSet<Block<T>>(pDominators[block.Successors[0]]);
                for (int i = 1; i < block.Successors.Count; i++)
                {
                    tmpPDominators.IntersectWith(pDominators[block.Successors[i]]);
                }
                tmpPDominators.Add(block);

                if (!tmpPDominators.SetEquals(pDominators[block]))
                {
                    pDominators[block] = tmpPDominators;
                    changed = true;
                }
            }
        }
    }

    private void CalculateDominators(ControlFlowGraph<T> graph)
    {
        dominators.Clear();
        foreach (var block in graph.Blocks)
        {
            if (block.BlockType == BlockType.Entry)
            {
                dominators[block] = new();
                dominators[block].Add(block);
            }
            else
            {
                dominators[block] = new HashSet<Block<T>>(graph.Blocks);
            }
        }


        bool changed = true;

        while (changed)
        {
            changed = false;

            foreach (var block in graph.Blocks)
            {
                if (block.BlockType == BlockType.Entry)
                    continue;


                // In a perfect world the entry block should be the only block with no predecessors
                // Our world isn't perfect thanks to the existance to jump tables and try catch with
                // the catch block being only reachable via exception handler magic 
                // We could bail out here but we could also just continue anyway
                // See: UnityEngine.AndroidJNISafe and look at cfg for any of the CallxxxxMethod methods
                // if (block.Predecessors.Count == 0)
                // {
                //      return;
                // }

                var tmpDominators = block.Predecessors.Count == 0 ? new HashSet<Block<T>>() : new HashSet<Block<T>>(dominators[block.Predecessors[0]]);
                for (int i = 1; i < block.Predecessors.Count; i++)
                {
                    tmpDominators.IntersectWith(dominators[block.Predecessors[i]]);
                }
                tmpDominators.Add(block);

                if (!tmpDominators.SetEquals(dominators[block]))
                {
                    dominators[block] = tmpDominators;
                    changed = true;
                }
            }
        }
    }
}
