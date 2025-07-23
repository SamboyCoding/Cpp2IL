using System.Collections.Generic;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Tests.Graphing;

public class BasicGraph
{
    ISILControlFlowGraph graph;

    [SetUp]
    public void Setup()
    {
        var instructions = new List<Instruction>();
        void Add(int index, OpCode opCode, params object[] operands) => instructions.Add(new Instruction(index, opCode, operands));

        Add(00, OpCode.ShiftStack, -40);
        Add(01, OpCode.CheckEqual, new Register(null, "zf"), new Register(null, "test1"), new Register(null, "test2"));
        Add(02, OpCode.Not, new Register(null, "zf"), new Register(null, "zf"));
        Add(03, OpCode.ConditionalJump, 7, new Register(null, "zf"));
        Add(04, OpCode.Move, new Register(null, "test3"), 0);
        Add(05, OpCode.Call, 0xDEADBEEF);
        Add(06, OpCode.Move, new Register(null, "test4"), 0);
        Add(07, OpCode.Move, new Register(null, "test5"), 0);
        Add(08, OpCode.CheckEqual, new Register(null, "zf"), new Register(null, "test1"), new Register(null, "test2"));
        Add(09, OpCode.ConditionalJump, 14, new Register(null, "zf"));
        Add(10, OpCode.CheckEqual, new Register(null, "zf"), new Register(null, "test1"), new Register(null, "test2"));
        Add(11, OpCode.Not, new Register(null, "zf"), new Register(null, "zf"));
        Add(12, OpCode.ConditionalJump, 14, new Register(null, "zf"));
        Add(13, OpCode.Call, 0xDEADBEEF);
        Add(14, OpCode.Move, new Register(null, "test4"), 0);
        Add(15, OpCode.Move, new Register(null, "test5"), 0);
        Add(16, OpCode.ShiftStack, 40);
        Add(17, OpCode.Call, 0xDEADBEEF);

        foreach (var instruction in instructions)
        {
            if (instruction.OpCode != OpCode.Jump && instruction.OpCode != OpCode.ConditionalJump)
                continue;
            instruction.Operands[0] = instructions[(int)instruction.Operands[0]];
        }

        graph = new ISILControlFlowGraph(instructions);
    }

    [Test]
    public void VerifyNumberOfBlocks()
    {
        Assert.That(graph.Blocks.Count == 9);
    }

    [Test]
    public void VerifyBlockEdges()
    {
        foreach (var block in graph.Blocks)
        {
            switch (block.BlockType)
            {
                case BlockType.Entry:
                    Assert.That(block.Predecessors.Count == 0);
                    Assert.That(block.Successors.Count > 0);
                    break;
                case BlockType.Exit:
                    Assert.That(block.Successors.Count == 0);
                    Assert.That(block.Predecessors.Count > 0);
                    break;
                default:
                    Assert.That(block.Successors.Count >= 1);
                    Assert.That(block.Predecessors.Count >= 1);
                    break;
            }
        }
    }
}
