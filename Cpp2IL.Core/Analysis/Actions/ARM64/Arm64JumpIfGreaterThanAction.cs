using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64JumpIfGreaterThanAction : BaseArm64ConditionalJumpAction
    {
        public Arm64JumpIfGreaterThanAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction, int destinationIndex) : base(context, instruction, destinationIndex)
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