using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibCpp2IL;

namespace Cpp2IL.Core.ISIL.Intermediate
{
    internal class LoadStringConverter : IInstructionConverter
    {
        public void ConvertIfNeeded(InstructionSetIndependentInstruction instruction)
        {
            if (instruction.OpCode != InstructionSetIndependentOpCode.Move)
            {
                return;
            }
            if (instruction.Operands[0].Type != InstructionSetIndependentOperand.OperandType.Register && instruction.Operands[1].Type != InstructionSetIndependentOperand.OperandType.Memory)
            {
                return;
            }
            var memoryOp = (IsilMemoryOperand)instruction.Operands[1].Data;
            if (memoryOp.Base == null && memoryOp.Index == null && memoryOp.Scale == 0)
            {
                var val = LibCpp2IlMain.GetLiteralByAddress((ulong)memoryOp.Addend);
                if (val == null)
                    return;

                instruction.Operands[1] = InstructionSetIndependentOperand.MakeImmediate(val);
            }
        }
    }
}
