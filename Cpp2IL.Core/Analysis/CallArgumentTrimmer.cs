using System.Linq;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

// Drop any operands beyond the known arguments to a method, e.g. if it was unresolved at ISIL gen time and we guessed 4 operands but it's only actually 2.
public static class CallArgumentTrimmer
{
    public static void Run(MethodAnalysisContext method)
    {
        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (!instruction.IsCall)
                continue;

            if (instruction.Operands[0] is MethodAnalysisContext called)
            {
                // target, [return value], [this], parameters...
                var expected = 1
                    + (instruction.OpCode == OpCode.Call ? 1 : 0)
                    + (called.IsStatic ? 0 : 1)
                    + called.Parameters.Count;

                for (var i = instruction.Operands.Count - 1; i >= expected; i--)
                    instruction.RemoveOperandAt(i);
            }
            else if (instruction.Operands[0] is Immediate)
            {
                // Unresolved call to immediate address (e.g. write barrier)
                // If return value is unused, the call has no managed effect and can be eliminated
                var retUsed = instruction.OpCode == OpCode.Call
                    && instruction.Operands.Count > 1
                    && instruction.Operands[1] is LocalVariable ret
                    && method.ControlFlowGraph.Instructions.Any(i => i != instruction && i.Operands.Contains(ret));

                if (!retUsed)
                {
                    instruction.OpCode = OpCode.Nop;
                    instruction.SetOperands();
                }
                else
                {
                    for (var i = instruction.Operands.Count - 1; i >= 2; i--)
                        instruction.RemoveOperandAt(i);
                }
            }
        }
    }
}
