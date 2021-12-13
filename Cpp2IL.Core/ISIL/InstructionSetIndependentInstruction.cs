using System;
using System.Collections.Generic;

namespace Cpp2IL.Core.ISIL;

public class InstructionSetIndependentInstruction
{
    public InstructionSetIndependentOpCode OpCode;
    public InstructionSetIndependentOperand[] Operands;
    
    public InstructionSetIndependentInstruction(InstructionSetIndependentOpCode opCode, params InstructionSetIndependentOperand[] operands)
    {
        OpCode = opCode;
        Operands = operands;

        OpCode.Validate(this);
    }

    public override string ToString() => $"{OpCode} {string.Join(", ", (IEnumerable<InstructionSetIndependentOperand>) Operands)}";
}