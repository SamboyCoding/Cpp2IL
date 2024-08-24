using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Tests;

public class GraphingTests
{
    ISILControlFlowGraph graph;
    [SetUp]
    public void Setup()
    {
        IsilBuilder isilBuilder = new IsilBuilder();

        isilBuilder.ShiftStack(0x0000, -40);
        isilBuilder.Compare(0x0001, InstructionSetIndependentOperand.MakeRegister("test1"), InstructionSetIndependentOperand.MakeRegister("test2"));
        isilBuilder.JumpIfNotEqual(0x0002, 0x0006);
        isilBuilder.Move(0x0003, InstructionSetIndependentOperand.MakeRegister("test3"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.Call(0x0004, 0xDEADBEEF);
        isilBuilder.Move(0x0005, InstructionSetIndependentOperand.MakeRegister("test4"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.Move(0x0006, InstructionSetIndependentOperand.MakeRegister("test5"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.Compare(0x0007, InstructionSetIndependentOperand.MakeRegister("test1"), InstructionSetIndependentOperand.MakeRegister("test2"));
        isilBuilder.JumpIfEqual(0x0008, 0x000C);
        isilBuilder.Compare(0x0009, InstructionSetIndependentOperand.MakeRegister("test1"), InstructionSetIndependentOperand.MakeRegister("test2"));
        isilBuilder.JumpIfNotEqual(0x000A, 0x000C);
        isilBuilder.Call(0x000B, 0xDEADBEEF);
        isilBuilder.Move(0x000C, InstructionSetIndependentOperand.MakeRegister("test4"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.Move(0x000D, InstructionSetIndependentOperand.MakeRegister("test5"), InstructionSetIndependentOperand.MakeImmediate(0));
        isilBuilder.ShiftStack(0x000E, 40);
        isilBuilder.Call(0x000F, 0xDEADBEEF);

        isilBuilder.FixJumps();

        graph = new();
        graph.Build(isilBuilder.BackingStatementList);
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
