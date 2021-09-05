using Cpp2IL.Core.Analysis.Actions.x86;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class JumpIfLessThanOrEqualToAction : BaseX86ConditionalJumpAction
    {
        public JumpIfLessThanOrEqualToAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            //All handled by base class
        }

        protected override string GetPseudocodeCondition()
        {
            //Invert condition, so greater than than, not <=
            return $"({GetArgumentOnePseudocodeValue()} > {GetArgumentTwoPseudocodeValue()})";
        }
        
        protected override string GetInvertedPseudocodeConditionForGotos()
        {
            return $"({GetArgumentOnePseudocodeValue()} <= {GetArgumentTwoPseudocodeValue()})";
        }

        protected override string GetTextSummaryCondition()
        {
            if (associatedCompare == null)
                return "the compare showed that it was less than or equal";

            return $"{associatedCompare.ArgumentOne} is less than or equal to {associatedCompare.ArgumentTwo}";
        }

        protected override OpCode GetJumpOpcode()
        {
            return OpCodes.Ble;
        }
    }
}