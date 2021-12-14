namespace Cpp2IL.Core.ISIL;

public struct IsilInstructionStatement : IsilStatement
{
    public readonly InstructionSetIndependentInstruction Instruction;

    public IsilInstructionStatement(InstructionSetIndependentInstruction instruction)
    {
        Instruction = instruction;
    }

    public override string ToString() => Instruction.ToString();
}