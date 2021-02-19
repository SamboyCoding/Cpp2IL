using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class JumpIfLessThanOrEqualToAction : ConditionalJumpAction
    {
        public JumpIfLessThanOrEqualToAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            //All handled by base class
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        protected override string GetPseudocodeCondition()
        {
            //Invert condition, so greater than than, not <=
            return $"({GetArgumentOnePseudocodeValue()} > {GetArgumentTwoPseudocodeValue()})";
        }

        protected override string GetTextSummaryCondition()
        {
            if (associatedCompare == null)
                return "the compare showed that it was less than or equal";

            return $"{associatedCompare.ArgumentOne} is less than or equal to {associatedCompare.ArgumentTwo}";
        }
    }
}