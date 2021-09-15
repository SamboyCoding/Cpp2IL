using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class ReturnFromFunctionAction : BaseAction<Instruction>
    {
        private IAnalysedOperand? returnValue;
        private bool _isVoid;

        public ReturnFromFunctionAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _isVoid = context.IsVoid();
            var returnType = context.ReturnType;
            returnValue = returnType.ShouldBeInFloatingPointRegister() ? context.GetOperandInRegister("xmm0") : context.GetOperandInRegister("rax");

            if (returnValue is LocalDefinition l)
                RegisterUsedLocal(l);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            if (!_isVoid)
            {
                if (returnValue == null)
                    throw new TaintedInstructionException();
                
                ret.AddRange(returnValue.GetILToLoad(context, processor));
            }
            
            ret.Add(processor.Create(OpCodes.Ret));

            return ret.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            if (_isVoid)
                return "return";
            
            return $"return {returnValue?.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            if (_isVoid)
                return "[!] Returns from the function\n";
            
            return $"[!] Returns {returnValue} from the function\n";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}