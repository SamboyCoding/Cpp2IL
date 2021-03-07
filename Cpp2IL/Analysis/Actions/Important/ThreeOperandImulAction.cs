using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class ThreeOperandImulAction : BaseAction
    {
        private IAnalysedOperand? _argOne;
        private IAnalysedOperand? _argTwo;
        private LocalDefinition _resultLocal;
        private string _destReg;

        public ThreeOperandImulAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            var argOneReg = Utils.GetRegisterNameNew(instruction.Op1Register);
            var argTwoReg = Utils.GetRegisterNameNew(instruction.Op2Register);

            if(!string.IsNullOrEmpty(argOneReg))
                _argOne = context.GetOperandInRegister(argOneReg);
            else
            {
                _argOne = context.MakeConstant(typeof(ulong), instruction.GetImmediate(1));
            }
            
            if(!string.IsNullOrEmpty(argTwoReg))
                _argTwo = context.GetOperandInRegister(argTwoReg);
            else
            {
                _argTwo = context.MakeConstant(typeof(ulong), instruction.GetImmediate(2));
            }
            
            if(_argOne is LocalDefinition l1)
                RegisterUsedLocal(l1);
            if(_argTwo is LocalDefinition l2)
                RegisterUsedLocal(l2);

            _resultLocal = context.MakeLocal(Utils.Int64Reference, reg: _destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            return $"{_resultLocal.Type} {_resultLocal.Name} = {_argOne?.GetPseudocodeRepresentation()} * {_argTwo?.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Multiplies {_argOne} and {_argTwo}, and stores the result in new local {_resultLocal} in register {_destReg}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}