using Cpp2IL.Analysis.ResultModels;
using SharpDisasm;

namespace Cpp2IL.Analysis.Actions
{
    public class ReturnFromFunctionAction : BaseAction
    {
        private IAnalysedOperand returnValue;
        
        public ReturnFromFunctionAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"return {returnValue}";
        }

        public override string ToTextSummary()
        {
            return $"Returns ${returnValue} from the function";
        }
    }
}