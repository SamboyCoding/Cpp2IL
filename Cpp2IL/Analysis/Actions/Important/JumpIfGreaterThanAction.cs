using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class JumpIfGreaterThanAction : ConditionalJumpAction
    {
        public JumpIfGreaterThanAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            //All handled by base class
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        protected override string GetPseudocodeCondition()
        {
            //Invert condition, so <=, not >
            return $"({GetArgumentOnePseudocodeValue()} <= {GetArgumentTwoPseudocodeValue()})";
        }

        protected override string GetTextSummaryCondition()
        {
            if (associatedCompare == null)
                return "the compare showed that it was greater than";

            return $"{associatedCompare.ArgumentOne} is greater than {associatedCompare.ArgumentTwo}";
        }
    }
}