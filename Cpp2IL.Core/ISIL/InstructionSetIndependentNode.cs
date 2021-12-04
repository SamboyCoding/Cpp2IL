using System.Collections.Generic;

namespace Cpp2IL.Core.ISIL;

public class InstructionSetIndependentNode
{
    public List<InstructionSetIndependentInstruction> Instructions = new();
    
    public void CompareEqual(InstructionSetIndependentOperand left, InstructionSetIndependentOperand right, long jumpToIfTrue) => 
        Instructions.Add(new(InstructionSetIndependentOpCode.CompareEqual, left, right, InstructionSetIndependentOperand.MakeMemory(new(jumpToIfTrue))));
}