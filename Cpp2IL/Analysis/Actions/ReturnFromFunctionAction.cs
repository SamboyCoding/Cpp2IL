using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class ReturnFromFunctionAction : BaseAction
    {
        private IAnalysedOperand returnValue;
        private bool _isVoid;

        public ReturnFromFunctionAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _isVoid = context.IsVoid();
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            if (_isVoid)
                return "return";
            
            return $"return {returnValue}";
        }

        public override string ToTextSummary()
        {
            if (_isVoid)
                return "Returns from the function\n";
            
            return $"Returns {returnValue} from the function\n";
        }
    }
}