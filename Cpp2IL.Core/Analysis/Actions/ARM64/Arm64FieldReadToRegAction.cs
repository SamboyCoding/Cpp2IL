using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64FieldReadToRegAction : AbstractFieldReadAction<Arm64Instruction>
    {
        public Arm64FieldReadToRegAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction)
            : this
            (
                context,
                instruction,
                Utils.Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id), (ulong) instruction.MemoryOffset(),
                Utils.Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id)
            )
        {
        }

        public Arm64FieldReadToRegAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction, string sourceReg, ulong offset, string destReg) : base(context, instruction)
        {
            ReadFrom = context.GetLocalInReg(sourceReg);

            if (ReadFrom?.Type == null)
                return;

            FieldRead = FieldUtils.GetFieldBeingAccessed(ReadFrom.Type, offset, destReg[0] == 'v');

            if (FieldRead == null)
                return;

            RegisterUsedLocal(ReadFrom, context);

            LocalWritten = context.MakeLocal(FieldRead.GetFinalType()!, reg: destReg);

            RegisterUsedLocal(LocalWritten, context);
        }
    }
}