using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.Actions.x86.Important;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public abstract class BaseX86ConditionalJumpAction : AbstractConditionalJumpAction<Instruction>
    {
        protected BaseX86ConditionalJumpAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction.NearBranchTarget, instruction)
        {
        }

        protected sealed override bool IsExceptionThrowWhichIsImplicitInCSharp()
        {
            var body = X86Utils.GetMethodBodyAtVirtAddressNew(JumpTarget, true);

            if (body.Count > 0 && body[0].Mnemonic == Mnemonic.Call && CallExceptionThrowerFunction.IsExceptionThrower(body[0].NearBranchTarget))
            {
                var exceptionThrown = CallExceptionThrowerFunction.GetExceptionThrown(body[0].NearBranchTarget)?.Name;
                if (exceptionThrown is "NullReferenceException" or "IndexOutOfRangeException" or "ArrayTypeMismatchException")
                {
                    return true;
                }
            }

            if (body.Count > 3 && body[0].Mnemonic == Mnemonic.Mov && body[1].Mnemonic == Mnemonic.Xor && body[2].Mnemonic == Mnemonic.Call && CallExceptionThrowerFunction.IsExceptionThrower(body[2].NearBranchTarget))
            {
                if (CallExceptionThrowerFunction.GetExceptionThrown(body[2].NearBranchTarget)?.Name == "IndexOutOfRangeException")
                {
                    return true;
                }
            }


            if (body.Count > 1 && body[0].Mnemonic == Mnemonic.Xor && body[1].Mnemonic == Mnemonic.Call && CallExceptionThrowerFunction.IsExceptionThrower(body[1].NearBranchTarget))
            {
                if (CallExceptionThrowerFunction.GetExceptionThrown(body[1].NearBranchTarget)?.Name == "NullReferenceException")
                {
                    return true;
                }
            }

            return false;
        }

        protected override bool IsArrayTypeCheck(MethodAnalysis<Instruction> context)
        {
            //We know we have a normal if statement here (not a goto) so we're jumping over the block
            //So, check for any call to Object::IsInst in our block
            var block = X86Utils.GetMethodBodyAtVirtAddressNew(AssociatedInstruction.NextIP, true);
            if (block.Any(i => i.Mnemonic == Mnemonic.Call && i.NearBranchTarget == context.KeyFunctionAddresses.il2cpp_vm_object_is_inst))
                return true;

            return false;
        }
    }
}