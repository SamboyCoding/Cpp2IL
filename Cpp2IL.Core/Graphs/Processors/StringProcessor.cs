using Cpp2IL.Core.ISIL;
using LibCpp2IL;

namespace Cpp2IL.Core.Graphs.Processors
{
    internal class StringProcessor : IBlockProcessor
    {
        public void Process(Block block)
        {
            foreach (var instruction in block.isilInstructions)
            {
                // TODO: Check if it shows up in any other
                if (instruction.OpCode != InstructionSetIndependentOpCode.Move)
                {
                    return;
                }
                if (instruction.Operands[0].Type != InstructionSetIndependentOperand.OperandType.Register || instruction.Operands[1].Type != InstructionSetIndependentOperand.OperandType.Memory)
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
}
