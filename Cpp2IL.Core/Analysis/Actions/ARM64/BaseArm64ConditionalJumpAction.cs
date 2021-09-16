using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public abstract class BaseArm64ConditionalJumpAction : AbstractConditionalJumpAction<Arm64Instruction>
    {
        protected BaseArm64ConditionalJumpAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction associatedInstruction, int jumpDestinationIndex) 
            : base(context, (ulong)associatedInstruction.Details.Operands[jumpDestinationIndex].Immediate, associatedInstruction)
        {
        }

        protected sealed override bool IsImplicitNRE()
        {
            //TODO
            return false;
        }
    }
}