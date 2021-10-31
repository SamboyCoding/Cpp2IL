using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64ComparisonAction : AbstractComparisonAction<Arm64Instruction>
    {
        public Arm64ComparisonAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction associatedInstruction, bool skipSecond = false) : base(context, associatedInstruction, skipSecond)
        {
        }

        protected override bool IsMemoryReferenceAnAbsolutePointer(Arm64Instruction instruction, int operandIdx) => instruction.Details.Operands[operandIdx].IsImmediate();

        protected override string GetRegisterName(Arm64Instruction instruction, int opIdx) => Utils.Utils.GetRegisterNameNew(instruction.Details.Operands[opIdx].RegisterSafe()?.Id ?? Arm64RegisterId.Invalid);

        protected override string GetMemoryBaseName(Arm64Instruction instruction) => Utils.Utils.GetRegisterNameNew(instruction.MemoryBase()?.Id ?? Arm64RegisterId.Invalid);

        protected override ulong GetInstructionMemoryOffset(Arm64Instruction instruction) => (ulong)instruction.MemoryOffset();

        //ARM64 encodes memory addresses as immediate values. I think.
        protected override ulong GetMemoryPointer(Arm64Instruction instruction, int operandIdx) => GetImmediateValue(instruction, operandIdx);

        protected override ulong GetImmediateValue(Arm64Instruction instruction, int operandIdx) => (ulong)instruction.Details.Operands[operandIdx].ImmediateSafe();

        protected override ComparisonOperandType GetOperandType(Arm64Instruction instruction, int operandIdx)
        {
            var operand = instruction.Details.Operands[operandIdx];

            if (operand.Type == Arm64OperandType.Register)
                return ComparisonOperandType.REGISTER_CONTENT;

            if (operand.Type == Arm64OperandType.Memory)
                return ComparisonOperandType.MEMORY_ADDRESS_OR_OFFSET;
            
            if (operand.IsImmediate() && LibCpp2IlMain.Binary!.TryMapVirtualAddressToRaw((ulong)operand.Immediate, out _))
                return ComparisonOperandType.MEMORY_ADDRESS_OR_OFFSET;

            return ComparisonOperandType.IMMEDIATE_CONSTANT;
        }
    }
}