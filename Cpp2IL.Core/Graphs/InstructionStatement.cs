namespace Cpp2IL.Core.Graphs;

public class InstructionStatement<TInstruction> : IStatement
{
    public TInstruction Instruction { get; }
    public InstructionStatement(TInstruction instruction)
    {
        Instruction = instruction;
    }
    public string GetTextDump(int indent)
    {
        return new string(' ', indent) + Instruction+"\n";
    }
}