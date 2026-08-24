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
            if (!instruction.IsCall || instruction.Operands[0] is not MethodAnalysisContext called)
                continue;

            // target, [return value], [this], parameters...
            var expected = 1
                + (instruction.OpCode == OpCode.Call ? 1 : 0)
                + (called.IsStatic ? 0 : 1)
                + called.Parameters.Count;

            for (var i = instruction.Operands.Count - 1; i >= expected; i--)
                instruction.RemoveOperandAt(i);
        }
    }
}
