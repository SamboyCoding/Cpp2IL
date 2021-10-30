using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class ImmediateToArrayAction : AbstractArrayOffsetWriteAction<Instruction>
    {
        private readonly int _offset;
        private readonly ulong _immediateValue;

        public ImmediateToArrayAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var memReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            TheArray = context.GetLocalInReg(memReg);

            if(TheArray?.KnownInitialValue is not AllocatedArray array)
                return;

            _immediateValue = instruction.GetImmediate(1);

            _offset = (int) (instruction.MemoryDisplacement32 - Il2CppArrayUtils.FirstItemOffset) / Utils.GetPointerSizeBytes();

            array.KnownValuesAtOffsets[_offset] = _immediateValue;
        }

        protected override int GetOffsetWritten() => _offset;

        protected override string? GetPseudocodeValue() => _immediateValue.ToString();

        protected override string? GetSummaryValue() => _immediateValue.ToString();

        protected override Mono.Cecil.Cil.Instruction[] GetInstructionsToLoadValue(MethodAnalysis<Instruction> context, ILProcessor processor) => new[]
        {
            processor.Create(OpCodes.Ldc_I8, (long) _immediateValue),
            processor.Create(OpCodes.Conv_U8),
        };
    }
}