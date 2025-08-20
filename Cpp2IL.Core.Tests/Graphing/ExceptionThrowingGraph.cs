using System.Collections.Generic;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Tests.Graphing;

public class ExceptionThrowingGraph
{
    ISILControlFlowGraph graph;

    [SetUp]
    public void Setup()
    {
        var instructions = new List<Instruction>();
        void Add(int index, OpCode opCode, params object[] operands) => instructions.Add(new Instruction(index, opCode, operands));

        Add(001, OpCode.ShiftStack, -8);
        Add(002, OpCode.Move, new StackOffset(0), new Register(null, "reg1"));
        Add(003, OpCode.ShiftStack, -80);
        Add(004, OpCode.CheckEqual, new Register(null, "zf"), new Register(null, "reg2"), 0);
        Add(005, OpCode.Move, new Register(null, "reg3"), new Register(null, "reg4"));
        Add(006, OpCode.Not, new Register(null, "zf"), new Register(null, "zf"));
        Add(007, OpCode.ConditionalJump, 11, new Register(null, "zf"));
        Add(008, OpCode.Move, new Register(null, "reg5"), 0);
        Add(009, OpCode.Call, 0xDEADBEEF);
        Add(010, OpCode.Move, new Register(null, "reg6"), 1);
        Add(011, OpCode.CheckEqual, new Register(null, "zf"), new Register(null, "reg7"), 0);
        Add(012, OpCode.ConditionalJump, 38, new Register(null, "zf"));
        Add(013, OpCode.Move, new Register(null, "reg8"), 1);
        Add(014, OpCode.Move, new Register(null, "reg9"), 2);
        Add(015, OpCode.Move, new Register(null, "reg10"), 3);
        Add(016, OpCode.Move, new StackOffset(0x40), new Register(null, "reg11"));
        Add(017, OpCode.Move, new Register(null, "reg12"), "input");
        Add(018, OpCode.Move, new StackOffset(0x30), new Register(null, "reg13"));
        Add(019, OpCode.CheckEqual, new Register(null, "zf"), new Register(null, "reg14"), 2);
        Add(020, OpCode.Move, new StackOffset(0x20), new Register(null, "reg15"));
        Add(021, OpCode.Move, new StackOffset(0x40), 1);
        Add(022, OpCode.Move, new StackOffset(0x38), new Register(null, "reg16"));
        Add(023, OpCode.ConditionalJump, 28, new Register(null, "zf"));
        Add(024, OpCode.CheckEqual, new Register(null, "zf"), new MemoryOperand(new Register(null, "reg17"), addend: 224), 0);
        Add(025, OpCode.Not, new Register(null, "zf"), new Register(null, "zf"));
        Add(026, OpCode.ConditionalJump, 28, new Register(null, "zf"));
        Add(027, OpCode.Call, 0xDEADBEEF);
        Add(028, OpCode.Move, new Register(null, "reg18"), 0);
        Add(029, OpCode.Move, new Register(null, "reg19"), new StackOffset(0x20));
        Add(030, OpCode.Move, new Register(null, "reg20"), new Register(null, "reg21"));
        Add(031, OpCode.Call, 0xDEADBEEF);
        Add(032, OpCode.CheckEqual, new Register(null, "zf"), new Register(null, "reg22"), 0);
        Add(033, OpCode.ConditionalJump, 50, new Register(null, "zf"));
        Add(034, OpCode.Move, new Register(null, "reg23"), new StackOffset(0x20));
        Add(035, OpCode.ShiftStack, 80);
        Add(036, OpCode.Move, new Register(null, "reg24"), new StackOffset(0));
        Add(037, OpCode.ShiftStack, 8);
        Add(038, OpCode.Return, new Register(null, "reg25"));
        Add(039, OpCode.Move, new Register(null, "reg26"), 0);
        Add(040, OpCode.Call, 0xDEADBEEF);
        Add(041, OpCode.Move, new Register(null, "reg27"), "input");
        Add(042, OpCode.Move, new Register(null, "reg28"), 0);
        Add(043, OpCode.Move, new Register(null, "reg29"), new Register(null, "reg30"));
        Add(044, OpCode.Move, new Register(null, "reg31"), new Register(null, "reg32"));
        Add(045, OpCode.Call, 0xDEADBEEF);
        Add(046, OpCode.Move, new Register(null, "reg33"), new MemoryOperand(addend: 0xDEADBEEF));
        Add(047, OpCode.Move, new Register(null, "reg34"), new Register(null, "reg35"));
        Add(048, OpCode.Call, 0xDEADBEEF);
        Add(049, OpCode.Interrupt);
        Add(050, OpCode.Move, new Register(null, "reg36"), 0);
        Add(051, OpCode.Move, new Register(null, "reg37"), new StackOffset(0x20));
        Add(052, OpCode.Call, 0xDEADBEEF);
        Add(053, OpCode.Move, new Register(null, "reg38"), new MemoryOperand(addend: 0x1809C39E0));
        Add(054, OpCode.Move, new Register(null, "reg39"), new Register(null, "reg40"));
        Add(055, OpCode.Call, 0xDEADBEEF);

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
        Assert.That(graph.Blocks.Count == 19);
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

