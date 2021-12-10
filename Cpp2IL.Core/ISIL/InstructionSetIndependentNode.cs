using System.Collections.Generic;

namespace Cpp2IL.Core.ISIL;

public class InstructionSetIndependentNode
{
    public List<IsilStatement> Statements = new();

    private void AddInstruction(InstructionSetIndependentInstruction instruction) => Statements.Add(new IsilInstructionStatement(instruction));
    
    public void CompareEqual(InstructionSetIndependentOperand left, InstructionSetIndependentOperand right, long jumpToIfTrue) => 
        AddInstruction(new(InstructionSetIndependentOpCode.CompareEqual, left, right, InstructionSetIndependentOperand.MakeMemory(new(jumpToIfTrue))));
}