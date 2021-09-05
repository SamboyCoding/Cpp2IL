using Cpp2IL.Core.Analysis.Actions.x86;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class JumpIfGreaterThanAction : BaseX86ConditionalJumpAction
    {
        public JumpIfGreaterThanAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            //All handled by base class
        }

        protected override string GetPseudocodeCondition()
        {
            //Invert condition, so <=, not >
            return $"({GetArgumentOnePseudocodeValue()} <= {GetArgumentTwoPseudocodeValue()})";
        }

        protected override string GetInvertedPseudocodeConditionForGotos()
        {
            return $"({GetArgumentOnePseudocodeValue()} > {GetArgumentTwoPseudocodeValue()})";
        }

        protected override OpCode GetJumpOpcode()
        {
            return OpCodes.Bgt;
        }

        protected override string GetTextSummaryCondition()
        {
            if (associatedCompare == null)
                return "the compare showed that it was greater than";

            return $"{associatedCompare.ArgumentOne} is greater than {associatedCompare.ArgumentTwo}";
        }
    }
}