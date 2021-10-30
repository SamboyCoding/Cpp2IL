using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.PostProcessActions.ILPostProcess
{
    public class OptimiseLocalsPostProcessor<T> : ILPostProcessor<T>
    {
        public override void PostProcess(MethodAnalysis<T> analysis, MethodBody body)
        {
            var variableUsageCount = body.Variables.ToDictionary(v => v, v => 0);

            //Two passes
            //First => usage count (excluding stlocs)
            //Second => Remove any IL and variables where IL is
            //stloc x
            //ldloc x
            //And count is 1

            foreach (var instruction in body.Instructions)
                if (instruction.Operand is VariableDefinition variable && instruction.OpCode != OpCodes.Stloc)
                    variableUsageCount[variable]++;

            foreach (var (variable, count) in variableUsageCount)
            {
                if (count != 1) continue;

                //Search through body for IL pattern
                for (var i = 0; i < body.Instructions.Count; i++)
                {
                    var insn = body.Instructions[i];

                    if (insn.OpCode != OpCodes.Stloc || insn.Next?.OpCode != OpCodes.Ldloc)
                        continue;

                    //We have an ldloc stloc pair
                    //Check operands are equal to variable
                    if (insn.Operand != insn.Next.Operand || insn.Operand != variable)
                        continue;

                    //Remove stloc ldloc and the variable
                    //Which means we have to remove one from i too
                    body.Instructions.RemoveAt(i);
                    body.Instructions.RemoveAt(i);
                    body.Variables.Remove(variable);
                    i--;
                }
            }
        }
    }
}