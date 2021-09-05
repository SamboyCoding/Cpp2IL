using Cpp2IL.Core.Analysis.Actions.x86;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class JumpIfGreaterThanOrEqualToAction : BaseX86ConditionalJumpAction
    {
        public JumpIfGreaterThanOrEqualToAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            //All handled by base class
        }

        protected override string GetPseudocodeCondition()
        {
            //Invert condition, so less than, not >=
            return $"({GetArgumentOnePseudocodeValue()} < {GetArgumentTwoPseudocodeValue()})";
        }
        
        protected override string GetInvertedPseudocodeConditionForGotos()
        {
            return $"({GetArgumentOnePseudocodeValue()} >= {GetArgumentTwoPseudocodeValue()})";
        }

        protected override string GetTextSummaryCondition()
        {
            if (associatedCompare == null)
                return "the compare showed that it was greater than or equal";

            return $"{associatedCompare.ArgumentOne} is greater than or equal to {associatedCompare.ArgumentTwo}";
        }

        protected override OpCode GetJumpOpcode()
        {
            return OpCodes.Bge;
        }
    }
}