using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class SubtractRegFromRegAction : BaseAction
    {
        private LocalDefinition? _firstOp;
        private IAnalysedOperand? _secondOp;
        
        public SubtractRegFromRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
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
            if (_firstOp == null || _secondOp == null)
                throw new TaintedInstructionException("Missing an argument");
            
            List<Mono.Cecil.Cil.Instruction> ret = new List<Mono.Cecil.Cil.Instruction>();
            
            //Load arg one
            ret.AddRange(_firstOp.GetILToLoad(context, processor));
            
            //Load arg two
            ret.AddRange(_secondOp.GetILToLoad(context, processor));
            
            //Subtract
            ret.Add(processor.Create(OpCodes.Sub));

            //Set local
            ret.Add(processor.Create(OpCodes.Stloc, _firstOp.Variable));

            return ret.ToArray();
        }

        public override string ToPsuedoCode()
        {
            return $"{_firstOp?.Name} -= {_secondOp?.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Subtracts {_secondOp} from {_firstOp} and stores the result in {_firstOp}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}