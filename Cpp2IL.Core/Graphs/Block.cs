using System.Text;
using System.Collections.Generic;

namespace Cpp2IL.Core.Graphs;

public class Block<Instruction> where Instruction : notnull
{
    public BlockType BlockType { get; set; } = BlockType.Unknown;
    public List<Block<Instruction>> Predecessors = [];
    public List<Block<Instruction>> Successors = [];

    public List<Instruction> Instructions = [];

    public bool Dirty { get; set; }

    public override string ToString()
    {
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("Type: " + BlockType);
        stringBuilder.AppendLine();
        foreach (var instruction in Instructions)
        {
            stringBuilder.AppendLine(instruction.ToString());
        }
        return stringBuilder.ToString();
    }

    public void AddInstruction(Instruction instruction)
    {
        Instructions.Add(instruction);
    }
}
