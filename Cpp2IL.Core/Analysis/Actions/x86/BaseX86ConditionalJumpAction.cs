using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.Actions.x86.Important;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public abstract class BaseX86ConditionalJumpAction : AbstractConditionalJumpAction<Instruction>
    {
        protected BaseX86ConditionalJumpAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction.NearBranchTarget, instruction)
        {
        }

        protected sealed override bool IsImplicitNRE()
        {
            var body = Utils.GetMethodBodyAtVirtAddressNew(JumpTarget, true);

            if (body.Count > 0 && body[0].Mnemonic == Mnemonic.Call && CallExceptionThrowerFunction.IsExceptionThrower(body[0].NearBranchTarget))
            {
                if (CallExceptionThrowerFunction.GetExceptionThrown(body[0].NearBranchTarget)?.Name == "NullReferenceException")
                {
                    return true;
                }
            }

            return false;
        }
    }
}