using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class StaticFieldToRegAction : AbstractStaticFieldReadAction<Instruction>
    {
        public StaticFieldToRegAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var fieldsPtrConst = context.GetConstantInReg(Utils.Utils.GetRegisterNameNew(instruction.MemoryBase));
            string destReg = Utils.Utils.GetRegisterNameNew(instruction.Op0Register);

            if (fieldsPtrConst == null || fieldsPtrConst.Type != typeof(StaticFieldsPtr)) return;

            var fieldsPtr = (StaticFieldsPtr) fieldsPtrConst.Value;

            FieldRead = FieldUtils.GetStaticFieldByOffset(fieldsPtr, instruction.MemoryDisplacement32);
            
            if (FieldRead == null) return;

            LocalWritten = context.MakeLocal(FieldRead.FieldType, reg: destReg);
        }
    }
}