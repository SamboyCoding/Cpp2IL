using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
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
            var body = MiscUtils.GetArm64MethodBodyAtVirtualAddress(JumpTarget, true, 3);

            for (var i = 0; i < Math.Min(3, body.Count); i++)
            {
                if (body[i].Mnemonic is "b" or "bl" && body[i].Details.Operands[0].IsImmediate() && Arm64CallThrowHelperAction.IsThrowHelper(body[i].Details.Operands[0].Immediate))
                    if (Arm64CallThrowHelperAction.GetExceptionThrown(body[i].Details.Operands[0].Immediate)?.Name == "NullReferenceException")
                        return true;
            }

            return false;
        }
    }
}