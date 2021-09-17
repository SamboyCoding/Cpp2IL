using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64ImmediateToFieldAction : AbstractFieldWriteAction<Arm64Instruction>
    {
        private long _immValue;

        public Arm64ImmediateToFieldAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            var memReg = Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id);
            InstanceBeingSetOn = context.GetLocalInReg(memReg);

            _immValue = instruction.Details.Operands[0].Immediate;

            if(InstanceBeingSetOn?.Type == null)
                return;
            
            RegisterUsedLocal(InstanceBeingSetOn);

            FieldWritten = FieldUtils.GetFieldBeingAccessed(InstanceBeingSetOn.Type, (ulong)instruction.MemoryOffset(), false);
        }

        protected override string? GetValueSummary() => _immValue.ToString();

        protected override string? GetValuePseudocode() => _immValue.ToString();

        protected override Instruction[] GetIlToLoadValue(MethodAnalysis<Arm64Instruction> context, ILProcessor processor) => new[]
        {
            processor.Create(OpCodes.Ldc_I4, (int) _immValue),
        };
    }
}