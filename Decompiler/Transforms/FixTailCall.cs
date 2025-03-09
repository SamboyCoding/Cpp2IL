using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Adds return instruction if the last instruction is call.
/// </summary>
public class FixTailCall : ITransform
{
    public void Apply(Method method)
    {
        foreach (var predecessor in method.ControlFlowGraph.ExitBlock.Predecessors)
        {
            if (!predecessor.IsCall) continue;
            var returnValue = predecessor.Instructions.Last().Operands[0];

            if (returnValue == null)
                predecessor.Instructions.Add(new Instruction(-1, OpCode.Return));
            else
                predecessor.Instructions.Add(new Instruction(-1, OpCode.Return, returnValue));
        }
    }
}
