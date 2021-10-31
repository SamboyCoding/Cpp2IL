using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Iced.Intel;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class RegToStaticFieldAction : AbstractStaticFieldWriteAction<Instruction>
    {
        public RegToStaticFieldAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _sourceOperand = context.GetOperandInRegister(Utils.Utils.GetRegisterNameNew(instruction.Op1Register));
            var destStaticFieldsPtr = context.GetConstantInReg(Utils.Utils.GetRegisterNameNew(instruction.MemoryBase));
            var staticFieldOffset = instruction.MemoryDisplacement32;

            if (destStaticFieldsPtr?.Value is not StaticFieldsPtr staticFieldsPtr) 
                return;

            if (_sourceOperand is LocalDefinition l)
                RegisterUsedLocal(l, context);

            _theField = FieldUtils.GetStaticFieldByOffset(staticFieldsPtr, staticFieldOffset);
        }
    }
}