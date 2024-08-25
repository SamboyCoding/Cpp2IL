using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Tests.Graphing;
public class ExceptionThrowingGraph
{
    ISILControlFlowGraph graph;

    [SetUp]
    public void Setup()
    {
        var isilBuilder = new IsilBuilder();

        isilBuilder.Push(001, InstructionSetIndependentOperand.MakeRegister("sp"), InstructionSetIndependentOperand.MakeRegister("reg1"));
        isilBuilder.ShiftStack(002, -80);
        isilBuilder.Compare(003, InstructionSetIndependentOperand.MakeRegister("reg2"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.Move(004, InstructionSetIndependentOperand.MakeRegister("reg3"), InstructionSetIndependentOperand.MakeRegister("reg4"));
        isilBuilder.JumpIfNotEqual(005, 9);
        isilBuilder.Move(006, InstructionSetIndependentOperand.MakeRegister("reg5"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.Call(007, 0xDEADBEEF);
        isilBuilder.Move(008, InstructionSetIndependentOperand.MakeRegister("reg6"), InstructionSetIndependentOperand.MakeImmediate(1));
        isilBuilder.Compare(009, InstructionSetIndependentOperand.MakeRegister("reg7"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.JumpIfEqual(010, 35);
        isilBuilder.Move(011, InstructionSetIndependentOperand.MakeRegister("reg8"), InstructionSetIndependentOperand.MakeImmediate(1));
        isilBuilder.Move(012, InstructionSetIndependentOperand.MakeRegister("reg9"), InstructionSetIndependentOperand.MakeImmediate(2));
        isilBuilder.Move(013, InstructionSetIndependentOperand.MakeRegister("reg10"), InstructionSetIndependentOperand.MakeImmediate(3));
        isilBuilder.Move(014, InstructionSetIndependentOperand.MakeStack(0x40), InstructionSetIndependentOperand.MakeRegister("reg11"));
        isilBuilder.Move(015, InstructionSetIndependentOperand.MakeRegister("reg12"), InstructionSetIndependentOperand.MakeImmediate("input"));
        isilBuilder.Move(016, InstructionSetIndependentOperand.MakeStack(0x30), InstructionSetIndependentOperand.MakeRegister("reg13"));
        isilBuilder.Compare(017, InstructionSetIndependentOperand.MakeRegister("reg14"), InstructionSetIndependentOperand.MakeImmediate(2));
        isilBuilder.Move(018, InstructionSetIndependentOperand.MakeStack(0x20), InstructionSetIndependentOperand.MakeRegister("reg15"));
        isilBuilder.Move(019, InstructionSetIndependentOperand.MakeStack(0x40), InstructionSetIndependentOperand.MakeImmediate(1));
        isilBuilder.Move(020, InstructionSetIndependentOperand.MakeStack(0x38), InstructionSetIndependentOperand.MakeRegister("reg16"));
        isilBuilder.JumpIfEqual(021, 25);
        isilBuilder.Compare(022, InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(InstructionSetIndependentOperand.MakeRegister("reg17"), 224)), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.JumpIfNotEqual(023, 25);
        isilBuilder.Call(024, 0xDEADBEEF);
        isilBuilder.Move(025, InstructionSetIndependentOperand.MakeRegister("reg18"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.LoadAddress(026, InstructionSetIndependentOperand.MakeRegister("reg19"), InstructionSetIndependentOperand.MakeStack(0x20));
        isilBuilder.Move(027, InstructionSetIndependentOperand.MakeRegister("reg20"), InstructionSetIndependentOperand.MakeRegister("reg21"));
        isilBuilder.Call(028, 0xDEADBEEF);
        isilBuilder.Compare(029, InstructionSetIndependentOperand.MakeRegister("reg22"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.JumpIfEqual(030, 46);
        isilBuilder.Move(031, InstructionSetIndependentOperand.MakeRegister("reg23"), InstructionSetIndependentOperand.MakeStack(0x20));
        isilBuilder.ShiftStack(032, 80);
        isilBuilder.Pop(033, InstructionSetIndependentOperand.MakeRegister("sp"), InstructionSetIndependentOperand.MakeRegister("reg24"));
        isilBuilder.Return(034, InstructionSetIndependentOperand.MakeRegister("reg25"));
        isilBuilder.Move(035, InstructionSetIndependentOperand.MakeRegister("reg26"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.Call(036, 0xDEADBEEF);
        isilBuilder.Move(037, InstructionSetIndependentOperand.MakeRegister("reg27"), InstructionSetIndependentOperand.MakeImmediate("input"));
        isilBuilder.Move(038, InstructionSetIndependentOperand.MakeRegister("reg28"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.Move(039, InstructionSetIndependentOperand.MakeRegister("reg29"), InstructionSetIndependentOperand.MakeRegister("reg30"));
        isilBuilder.Move(040, InstructionSetIndependentOperand.MakeRegister("reg31"), InstructionSetIndependentOperand.MakeRegister("reg32"));
        isilBuilder.Call(041, 0xDEADBEEF);
        isilBuilder.Move(042, InstructionSetIndependentOperand.MakeRegister("reg33"), InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(0xDEADBEEF)));
        isilBuilder.Move(043, InstructionSetIndependentOperand.MakeRegister("reg34"), InstructionSetIndependentOperand.MakeRegister("reg35"));
        isilBuilder.Call(044, 0xDEADBEEF);
        isilBuilder.Interrupt(045);
        isilBuilder.Move(046, InstructionSetIndependentOperand.MakeRegister("reg36"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.LoadAddress(047, InstructionSetIndependentOperand.MakeRegister("reg37"), InstructionSetIndependentOperand.MakeStack(0x20));
        isilBuilder.Call(048, 0xDEADBEEF);
        isilBuilder.Move(049, InstructionSetIndependentOperand.MakeRegister("reg38"), InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(0x1809C39E0)));
        isilBuilder.Move(050, InstructionSetIndependentOperand.MakeRegister("reg39"), InstructionSetIndependentOperand.MakeRegister("reg40"));
        isilBuilder.Call(051, 0xDEADBEEF);

        isilBuilder.FixJumps();

        graph = new();
        graph.Build(isilBuilder.BackingStatementList);
    }

    [Test]
    public void VerifyNumberOfBlocks()
    {
        Assert.That(graph.Blocks.Count == 18);
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

