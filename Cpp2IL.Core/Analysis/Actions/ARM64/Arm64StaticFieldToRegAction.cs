using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64StaticFieldToRegAction : AbstractStaticFieldReadAction<Arm64Instruction>
    {
        public Arm64StaticFieldToRegAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            var fieldsPtrConst = context.GetConstantInReg(Utils.Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id));
            string destReg = Utils.Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);

            if (fieldsPtrConst == null || fieldsPtrConst.Type != typeof(StaticFieldsPtr)) return;

            var fieldsPtr = (StaticFieldsPtr)fieldsPtrConst.Value;

            FieldRead = FieldUtils.GetStaticFieldByOffset(fieldsPtr, (uint)instruction.MemoryOffset());

            if (FieldRead == null) return;

            LocalWritten = context.MakeLocal(FieldRead.FieldType, reg: destReg);
        }
    }
}