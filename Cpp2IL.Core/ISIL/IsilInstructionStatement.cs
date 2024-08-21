namespace Cpp2IL.Core.ISIL;

public struct IsilInstructionStatement(InstructionSetIndependentInstruction instruction) : IsilStatement
{
    public readonly InstructionSetIndependentInstruction Instruction = instruction;

    public override string ToString() => Instruction.ToString();
}