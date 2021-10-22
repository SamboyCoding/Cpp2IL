using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64RegisterToStaticFieldAction : AbstractStaticFieldWriteAction<Arm64Instruction>
    {
        public readonly IAnalysedOperand? SourceOperand;

        public Arm64RegisterToStaticFieldAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            var sourceReg = Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);
            _sourceOperand = context.GetOperandInRegister(sourceReg);
            var destStaticFieldsPtr = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id));
            var staticFieldOffset = instruction.MemoryOffset();

            if (destStaticFieldsPtr?.Value is not StaticFieldsPtr staticFieldsPtr)
                return;

            if (_sourceOperand is LocalDefinition l)
                RegisterUsedLocal(l, context);

            _theField = FieldUtils.GetStaticFieldByOffset(staticFieldsPtr, (uint)staticFieldOffset);
        }
    }
}