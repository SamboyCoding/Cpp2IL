using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class ThrowAction : BaseAction
    {
        private IAnalysedOperand? exceptionToThrow;
        
        public ThrowAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            exceptionToThrow = context.GetOperandInRegister("rcx");
            
            if(exceptionToThrow is LocalDefinition l)
                RegisterUsedLocal(l);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"[!] Throws {exceptionToThrow}";
        }
    }
}