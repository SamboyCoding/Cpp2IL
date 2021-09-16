using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64JumpIfGreaterThanOrEqualToAction : BaseArm64ConditionalJumpAction
    {
        public Arm64JumpIfGreaterThanOrEqualToAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction, int destinationIndex) : base(context, instruction, destinationIndex)
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