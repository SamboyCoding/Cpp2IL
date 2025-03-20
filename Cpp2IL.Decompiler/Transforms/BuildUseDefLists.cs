namespace Cpp2IL.Decompiler.Transforms;

/// <summary>
/// Builds use-def lists for all blocks.
/// </summary>
public class BuildUseDefLists : ITransform
{
    public void Apply(Method method, IContext context)
    {
        var graph = method.ControlFlowGraph;

        foreach (var block in graph.Blocks)
        {
            var use = new List<object>();
            var def = new List<object>();

            foreach (var instruction in block.Instructions)
            {
                foreach (var operand in instruction.Sources.Where(operand => !use.Contains(operand)))
                    use.Add(operand);

                if (instruction.Destination != null && !def.Contains(instruction.Destination))
                    def.Add(instruction.Destination);
            }

            block.Use = use;
            block.Def = def;
        }
    }
}
