using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using SharpDisasm;

namespace Cpp2IL.Analysis.Actions
{
    public class CallManagedFunctionAction : BaseAction
    {
        private MethodDefinition? target;
        private List<IAnalysedOperand> arguments = new List<IAnalysedOperand>();
        
        public CallManagedFunctionAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var jumpTarget = Utils.GetJumpTarget(instruction, context.MethodStart + instruction.PC);
            SharedState.MethodsByAddress.TryGetValue(jumpTarget, out target);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Calls managed method {target?.FullName}";
        }
    }
}