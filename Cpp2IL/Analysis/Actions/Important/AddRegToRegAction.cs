using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class AddRegToRegAction : BaseAction
    {
        private LocalDefinition? _firstOp;
        private IAnalysedOperand? _secondOp;

        public AddRegToRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var firstReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            var secondReg = Utils.GetRegisterNameNew(instruction.Op1Register);

            _firstOp = context.GetLocalInReg(firstReg);
            _secondOp = context.GetOperandInRegister(secondReg);
            
            if(_firstOp != null)
                RegisterUsedLocal(_firstOp);
            
            if(_secondOp is LocalDefinition l)
                RegisterUsedLocal(l);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_firstOp?.Name} += {_secondOp?.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Adds {_firstOp} and {_secondOp} and stores the result in {_firstOp}";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}