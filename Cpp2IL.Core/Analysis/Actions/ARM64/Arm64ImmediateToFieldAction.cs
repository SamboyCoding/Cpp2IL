using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64ImmediateToFieldAction : AbstractFieldWriteAction<Arm64Instruction>
    {
        public readonly long ImmValue;

        public Arm64ImmediateToFieldAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            var memReg = Arm64Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id);
            InstanceBeingSetOn = context.GetLocalInReg(memReg);

            ImmValue = instruction.Details.Operands[0].Immediate;

            if(InstanceBeingSetOn?.Type == null)
                return;
            
            RegisterUsedLocal(InstanceBeingSetOn, context);

            FieldWritten = FieldUtils.GetFieldBeingAccessed(InstanceBeingSetOn.Type, (ulong)instruction.MemoryOffset(), false);
        }

        protected override string? GetValueSummary() => ImmValue.ToString();

        protected override string? GetValuePseudocode() => ImmValue.ToString();

        protected override Instruction[] GetIlToLoadValue(MethodAnalysis<Arm64Instruction> context, ILProcessor processor) => new[]
        {
            processor.Create(OpCodes.Ldc_I4, (int) ImmValue),
        };
    }
}