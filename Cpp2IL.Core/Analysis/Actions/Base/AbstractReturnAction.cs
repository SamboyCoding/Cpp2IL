using System.Collections.Generic;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractReturnAction<T> : BaseAction<T>
    {
        protected IAnalysedOperand? returnValue;
        protected bool _isVoid;

        protected AbstractReturnAction(MethodAnalysis<T> context, T instruction) : base(context, instruction)
        {
            _isVoid = context.IsVoid();
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
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