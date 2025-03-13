using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Builds use-def lists for all blocks.
/// </summary>
public class BuildUseDefLists : ITransform
{
    public void Apply(Method method)
    {
        var graph = method.ControlFlowGraph;

        foreach (var block in graph.Blocks)
        {
            var use = new List<IOperand>();
            var def = new List<IOperand>();

            foreach (var instruction in block.Instructions)
            {
                foreach (var operand in instruction.ReadOperands.Where(operand => !use.Contains(operand)))
                    use.Add(operand);

                foreach (var operand in instruction.WrittenOperands.Where(operand => !def.Contains(operand)))
                    def.Add(operand);
            }

            block.Use = use;
            block.Def = def;
        }
    }
}
