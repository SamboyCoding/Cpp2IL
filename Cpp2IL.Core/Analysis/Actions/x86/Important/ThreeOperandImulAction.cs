using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class ThreeOperandImulAction : BaseAction<Instruction>
    {
        private IAnalysedOperand? _argOne;
        private IAnalysedOperand? _argTwo;
        private LocalDefinition _resultLocal;
        private string _destReg;

        public ThreeOperandImulAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            var argOneReg = Utils.GetRegisterNameNew(instruction.Op1Register);
            var argTwoReg = Utils.GetRegisterNameNew(instruction.Op2Register);

            if(!string.IsNullOrEmpty(argOneReg))
                _argOne = context.GetOperandInRegister(argOneReg);
            else if(instruction.Op1Kind.IsImmediate())
            {
                //Note to self - this can be a field/memory operand as well.
                if (instruction.Op0Register.IsGPR32())
                    _argOne = context.MakeConstant(typeof(uint), (uint) (instruction.GetImmediate(1) & 0xFFFFFFFF));
                else
                    _argOne = context.MakeConstant(typeof(ulong), instruction.GetImmediate(1));
            }
            
            if(!string.IsNullOrEmpty(argTwoReg))
                _argTwo = context.GetOperandInRegister(argTwoReg);
            else if(instruction.Op2Kind.IsImmediate())
            {
                if (instruction.Op0Register.IsGPR32())
                    _argTwo = context.MakeConstant(typeof(uint), (uint) (instruction.GetImmediate(2) & 0xFFFFFFFF));
                else
                    _argTwo = context.MakeConstant(typeof(ulong), instruction.GetImmediate(2));
            }
            
            if(_argOne is LocalDefinition l1)
                RegisterUsedLocal(l1, context);
            if(_argTwo is LocalDefinition l2)
                RegisterUsedLocal(l2, context);

            _resultLocal = context.MakeLocal(Utils.Int64Reference, reg: _destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (_argOne == null || _argTwo == null)
                throw new TaintedInstructionException("Missing an argument");

            if (_resultLocal.Variable == null)
                throw new TaintedInstructionException("Destination local has been stripped");
            
            List<Mono.Cecil.Cil.Instruction> ret = new List<Mono.Cecil.Cil.Instruction>();
            
            //Load arg one
            ret.AddRange(_argOne.GetILToLoad(context, processor));
            
            //Load arg two
            ret.AddRange(_argTwo.GetILToLoad(context, processor));
            
            //Multiply
            ret.Add(processor.Create(OpCodes.Mul));

            //Set local
            ret.Add(processor.Create(OpCodes.Stloc, _resultLocal.Variable));

            return ret.ToArray();
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