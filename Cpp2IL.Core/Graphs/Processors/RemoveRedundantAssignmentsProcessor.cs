using System.Collections.Generic;
using Cpp2IL.Core.Graphs.Processors;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Graphs;

public class RemoveRedundantAssignmentsProcessor : IBlockProcessor
{
    public void Process(MethodAnalysisContext methodAnalysisContext, Block block)
    {
        // Repeat until no change
        var changed = true;
        while (changed)
        {
            changed = false;

            // For each instruction
            for (var i = 0; i < block.Instructions.Count - 1; i++)
            {
                var current = block.Instructions[i];
                var next = block.Instructions[i + 1];

                // Don't make return register same as args
                if (next.OpCode == OpCode.Call)
                    continue;

                if (current.OpCode != OpCode.Move) continue;
                if (current.Operands[0] is not Register moveDestRegister) continue;
                var moveSrc = current.Operands[1];

                // Check if it's used before being reassigned (+2 to skip next)
                if (IsRegisterUsedBeforeReassignment(i + 2, moveDestRegister, block))
                    continue;

                // Replace occurrences of the redundant register in the next instruction
                for (var j = 0; j < next.Operands.Count; j++)
                {
                    if (next.Operands[j] is Register register && register == moveDestRegister)
                    {
                        next.Operands[j] = moveSrc;
                        RemoveInstruction(current, methodAnalysisContext.ControlFlowGraph!);
                        changed = true;
                        break;
                    }
                }
            }
        }
    }

    private static bool IsRegisterUsedBeforeReassignment(int startIndex, Register register, Block block)
    {
        // Traverse everything reachable from current block
        var reachable = new HashSet<Block>();
        var worklist = new Queue<Block>();
        worklist.Enqueue(block);

        while (worklist.Count > 0)
        {
            var nextBlock = worklist.Dequeue();

            if (!reachable.Add(nextBlock))
                continue;

            for (int i = (nextBlock == block) ? startIndex : 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];

                // It's reassigned
                if (instruction.Destination is Register destRegister && destRegister == register)
                    return false;

                // is it used?
                foreach (var operand in instruction.Sources)
                {
                    if (operand is Register usedReg && usedReg == register)
                        return true;
                }
            }

            foreach (var succ in nextBlock.Successors)
                worklist.Enqueue(succ);
        }

        return false;
    }

    private static void RemoveInstruction(Instruction instruction, ISILControlFlowGraph graph)
    {
        var block = graph.FindBlockByInstruction(instruction);
        block?.Instructions.Remove(instruction);
    }
}
