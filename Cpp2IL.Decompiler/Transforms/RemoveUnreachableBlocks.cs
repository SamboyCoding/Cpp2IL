using Cpp2IL.Decompiler.ControlFlow;

namespace Cpp2IL.Decompiler.Transforms;

/// <summary>
/// Removes unreachable blocks from the control flow graph.
/// </summary>
public class RemoveUnreachableBlocks : ITransform
{
    public void Apply(Method method, IContext context)
    {
        var graph = method.ControlFlowGraph;

        if (graph.Blocks.Count == 0)
            return;

        // Get blocks reachable from entry
        var reachable = new List<Block>();
        var visited = new List<Block> { graph.EntryBlock };
        reachable.Add(graph.EntryBlock);

        var total = 0;
        while (total < reachable.Count)
        {
            var block = reachable[total];
            total++;

            foreach (var successor in block.Successors)
            {
                if (visited.Contains(successor))
                    continue;
                visited.Add(successor);
                reachable.Add(successor);
            }
        }

        // Get unreachable blocks
        var unreachable = graph.Blocks.Where(block => !visited.Remove(block)).ToList();

        // Remove those
        foreach (var block in unreachable)
        {
            // Don't remove entry or exit
            if (block == graph.EntryBlock || block == graph.ExitBlock)
                continue;

            block.Successors.Clear();
            graph.Blocks.Remove(block);
        }
    }
}
