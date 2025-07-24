using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Graphs;

public class Block
{
    public BlockType BlockType { get; set; } = BlockType.Unknown;
    public List<Block> Predecessors = [];
    public List<Block> Successors = [];

    public List<object> Use = [];
    public List<object> Def = [];

    public List<Instruction> Instructions = [];

    public int ID { get; set; } = -1;

    public bool Dirty { get; set; }
    public bool Visited = false;

    public override string ToString()
    {
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.AppendLine($"{BlockType} {ID}");
        stringBuilder.AppendLine();
        foreach (var instruction in Instructions)
        {
            stringBuilder.AppendLine(instruction.ToString());
        }

        return stringBuilder.ToString();
    }

    public void AddInstruction(Instruction instruction) => Instructions.Add(instruction);

    public void CalculateBlockType()
    {
        if (Instructions.Count <= 0)
            return;

        var instruction = Instructions.Last();

        BlockType = instruction.OpCode switch
        {
            OpCode.Jump => BlockType.OneWay,
            OpCode.ConditionalJump => BlockType.TwoWay,
            OpCode.IndirectJump => BlockType.NWay,
            OpCode.Call or OpCode.CallVoid => BlockType.Call,
            OpCode.Return => BlockType.Return,
            _ => BlockType.Fall,
        };

        if (BlockType == BlockType.Call && Successors.Count > 0)
        {
            if (Successors[0].BlockType == BlockType.Exit)
                BlockType = BlockType.TailCall;
        }
    }
}
