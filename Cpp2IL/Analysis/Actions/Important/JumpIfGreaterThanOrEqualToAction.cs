using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class JumpIfGreaterThanOrEqualToAction : ConditionalJumpAction
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