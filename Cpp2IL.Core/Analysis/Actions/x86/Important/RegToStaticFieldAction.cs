using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class RegToStaticFieldAction : AbstractStaticFieldWriteAction<Instruction>
    {
        public RegToStaticFieldAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _sourceOperand = context.GetOperandInRegister(Utils.GetRegisterNameNew(instruction.Op1Register));
            var destStaticFieldsPtr = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));
            var staticFieldOffset = instruction.MemoryDisplacement32;

            if (destStaticFieldsPtr?.Value is not StaticFieldsPtr staticFieldsPtr) 
                return;

            if (_sourceOperand is LocalDefinition l)
                RegisterUsedLocal(l);

            _theField = FieldUtils.GetStaticFieldByOffset(staticFieldsPtr, staticFieldOffset);
        }
    }
}