using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class ComparisonAction : AbstractComparisonAction<Instruction>
    {
        public ComparisonAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            
        }

        protected override bool IsMemoryReferenceAnAbsolutePointer(Instruction instruction, int operandIdx) => instruction.MemoryBase == Register.None || instruction.MemoryBase.GetFullRegister() == Register.RIP;

        protected override string GetRegisterName(Instruction instruction, int opIdx) => MiscUtils.GetRegisterNameNew(instruction.GetOpRegister(opIdx));

        protected override string GetMemoryBaseName(Instruction instruction) => MiscUtils.GetRegisterNameNew(instruction.MemoryBase);

        protected override ulong GetInstructionMemoryOffset(Instruction instruction) => instruction.MemoryDisplacement64;

        protected override ulong GetMemoryPointer(Instruction instruction, int operandIdx) => instruction.MemoryDisplacement64; 

        protected override ulong GetImmediateValue(Instruction instruction, int operandIdx) => instruction.GetImmediateSafe(operandIdx);

        protected override ComparisonOperandType GetOperandType(Instruction instruction, int operandIdx)
        {
            var opKind = instruction.GetOpKind(operandIdx);

            if (opKind.IsImmediate())
                return ComparisonOperandType.IMMEDIATE_CONSTANT;

            if (opKind == OpKind.Register)
                return ComparisonOperandType.REGISTER_CONTENT;

            return ComparisonOperandType.MEMORY_ADDRESS_OR_OFFSET;
        }
    }
}