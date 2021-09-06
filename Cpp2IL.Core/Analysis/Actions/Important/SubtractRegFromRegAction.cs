using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class SubtractRegFromRegAction : BaseAction<Instruction>
    {
        private LocalDefinition<Instruction>? _firstOp;
        private IAnalysedOperand<Instruction>? _secondOp;
        
        public SubtractRegFromRegAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var firstReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            var secondReg = Utils.GetRegisterNameNew(instruction.Op1Register);

            _firstOp = context.GetLocalInReg(firstReg);
            _secondOp = context.GetOperandInRegister(secondReg);
            
            if(_firstOp != null)
                RegisterUsedLocal(_firstOp);
            
            if(_secondOp is LocalDefinition<Instruction> l)
                RegisterUsedLocal(l);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (_firstOp == null || _secondOp == null)
                throw new TaintedInstructionException("Missing an argument");
            
            if (_firstOp.Variable == null)
                throw new TaintedInstructionException($"SubRFromR: First operand, {_firstOp}, has been stripped or has no variable");
            
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