using System;

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
}