using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64RegisterToFieldAction : AbstractFieldWriteFromVariableAction<Arm64Instruction>
    {
        public Arm64RegisterToFieldAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction)
            : this
            (
                context,
                instruction,
                Arm64Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id),
                Arm64Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id),
                (ulong) instruction.MemoryOffset()
            )
        {
        }

        public Arm64RegisterToFieldAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction, string memReg, string sourceReg, ulong memoryOffset) : base(context, instruction)
        {
            InstanceBeingSetOn = context.GetLocalInReg(memReg);
            SourceOperand = context.GetOperandInRegister(sourceReg);

            if (InstanceBeingSetOn?.Type == null)
                return;

            RegisterUsedLocal(InstanceBeingSetOn, context);

            if (SourceOperand is LocalDefinition l)
                RegisterUsedLocal(l, context);

            FieldWritten = FieldUtils.GetFieldBeingAccessed(InstanceBeingSetOn.Type, memoryOffset, sourceReg[0] == 'v');
        }

        protected override string? GetValueSummary() => SourceOperand?.ToString();

        protected override string? GetValuePseudocode() => SourceOperand?.GetPseudocodeRepresentation();
    }
}