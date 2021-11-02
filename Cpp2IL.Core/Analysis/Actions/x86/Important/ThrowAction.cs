using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class ThrowAction : BaseAction<Instruction>
    {
        private IAnalysedOperand? exceptionToThrow;
        
        public ThrowAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            exceptionToThrow = context.GetOperandInRegister("rcx");
            
            if(exceptionToThrow is LocalDefinition l)
                RegisterUsedLocal(l, context);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (exceptionToThrow is null)
                throw new TaintedInstructionException("Exception to throw is null");

            var ret = new List<Mono.Cecil.Cil.Instruction>();
            
            ret.AddRange(exceptionToThrow.GetILToLoad(context, processor));
            
            ret.Add(processor.Create(OpCodes.Throw));

            return ret.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"throw {exceptionToThrow?.GetPseudocodeRepresentation()}";
        }

        public override bool IsImportant()
        {
            return true;
        }

        public override string ToTextSummary()
        {
            return $"[!] Throws {exceptionToThrow}";
        }
    }
}