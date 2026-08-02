using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Tests.Analysis;

/// <summary>
/// Correctness tests for the dominator computation and SSA construction, exercising the two
/// shapes that the previous implementation got wrong: a diamond (single join) and a loop
/// (header reached by a back-edge).
/// </summary>
public class SsaAndDominators
{
    private static ISILControlFlowGraph BuildGraph(IReadOnlyList<Instruction> instructions)
    {
        // Resolve numeric jump targets to instruction references, as the real lifter does.
        foreach (var instruction in instructions)
        {
            if (instruction.OpCode is OpCode.Jump or OpCode.ConditionalJump)
                instruction.SetOperand(0, instructions[(int)instruction.Operands[0]]);
        }

        return new ISILControlFlowGraph(instructions.ToList());
    }

    private static List<Instruction> Diamond()
    {
        var instructions = new List<Instruction>();
        void Add(int index, OpCode opCode, params object[] operands) => instructions.Add(new Instruction(index, opCode, operands));

        Add(0, OpCode.Move, new Register(null, "x"), 0);                              // entry def of x
        Add(1, OpCode.ConditionalJump, 4, new Register(null, "cond"));               // branch
        Add(2, OpCode.Move, new Register(null, "x"), 1);                              // then: x = 1
        Add(3, OpCode.Jump, 5);
        Add(4, OpCode.Move, new Register(null, "x"), 2);                              // else: x = 2
        Add(5, OpCode.Move, new Register(null, "ret"), new Register(null, "x"));     // join: uses x
        Add(6, OpCode.Return, new Register(null, "ret"));

        return instructions;
    }

    private static List<Instruction> Loop()
    {
        var instructions = new List<Instruction>();
        void Add(int index, OpCode opCode, params object[] operands) => instructions.Add(new Instruction(index, opCode, operands));

        Add(0, OpCode.Move, new Register(null, "i"), 0);                             // pre-header: i = 0
        Add(1, OpCode.CheckLess, new Register(null, "cmp"), new Register(null, "i"), 10); // header: cmp = i < 10
        Add(2, OpCode.Not, new Register(null, "cmp"), new Register(null, "cmp"));
        Add(3, OpCode.ConditionalJump, 7, new Register(null, "cmp"));               // exit loop
        Add(4, OpCode.Add, new Register(null, "i"), new Register(null, "i"), 1);    // body: i = i + 1
        Add(5, OpCode.Call, 0xDEADBEEF);
        Add(6, OpCode.Jump, 1);                                                      // back-edge to header
        Add(7, OpCode.Return);

        return instructions;
    }

    private static Block BlockWith(ISILControlFlowGraph graph, Func<Instruction, bool> predicate)
        => graph.Blocks.First(b => b.Instructions.Any(predicate));

    private static List<Instruction> Phis(ISILControlFlowGraph graph)
        => graph.Blocks.SelectMany(b => b.Instructions).Where(i => i.OpCode == OpCode.Phi).ToList();

    private static string? RegName(object operand) => operand is Register r ? r.Name : null;

    [Test]
    public void DiamondDominatorsAreCorrect()
    {
        var graph = BuildGraph(Diamond());
        var dom = new DominatorInfo(graph);

        var branch = BlockWith(graph, i => i.OpCode == OpCode.ConditionalJump);
        var join = BlockWith(graph, i => i.OpCode == OpCode.Return);

        // The join is reached from both arms of the diamond...
        Assert.That(join.Predecessors.Count, Is.EqualTo(2));

        // ...so its immediate dominator is the branch block, not either arm.
        Assert.That(dom.ImmediateDominators[join], Is.EqualTo(branch));
        Assert.That(dom.Dominates(branch, join), Is.True);

        // Each arm has the join on its dominance frontier; the branch block does not.
        foreach (var arm in join.Predecessors)
        {
            Assert.That(arm, Is.Not.EqualTo(branch));
            Assert.That(dom.DominanceFrontier[arm], Does.Contain(join));
            Assert.That(dom.Dominates(arm, join), Is.False);
        }

        Assert.That(dom.DominanceFrontier[branch], Does.Not.Contain(join));
    }

    [Test]
    public void DiamondInsertsExactlyOnePhiAtTheJoin()
    {
        var graph = BuildGraph(Diamond());
        SsaForm.Build(graph, new DominatorInfo(graph));

        var join = BlockWith(graph, i => i.OpCode == OpCode.Return);
        var phis = Phis(graph);

        // x is defined on both arms and read at the join => exactly one phi, for x, at the join.
        Assert.That(phis.Count, Is.EqualTo(1));
        var phi = phis[0];
        Assert.That(join.Instructions, Does.Contain(phi));
        Assert.That(RegName(phi.Operands[0]), Is.EqualTo("x"));

        // One destination + one source per predecessor, positionally aligned.
        Assert.That(phi.Operands.Count, Is.EqualTo(1 + join.Predecessors.Count));

        // Every source is a version of x, and they are distinct (one per arm).
        var sources = phi.Operands.Skip(1).Cast<Register>().ToList();
        Assert.That(sources.All(s => s.Name == "x"), Is.True);
        Assert.That(sources.Select(s => s.Version).Distinct().Count(), Is.EqualTo(sources.Count));

        // The value read at the join is the phi's freshly-versioned result.
        var phiResult = (Register)phi.Operands[0];
        var useOfX = join.Instructions.First(i => i.OpCode == OpCode.Move && RegName(i.Operands[1]) == "x");
        Assert.That(((Register)useOfX.Operands[1]).Version, Is.EqualTo(phiResult.Version));
    }

    [Test]
    public void LoopInsertsHeaderPhiForCarriedVariable()
    {
        var graph = BuildGraph(Loop());
        SsaForm.Build(graph, new DominatorInfo(graph));

        // The header is the loop-carried join: it reads i, is reached by a back-edge, and so
        // has two predecessors (pre-header + latch).
        var header = BlockWith(graph, i => i.OpCode == OpCode.CheckLess);
        Assert.That(header.Predecessors.Count, Is.EqualTo(2));

        var iPhis = Phis(graph).Where(p => RegName(p.Operands[0]) == "i").ToList();
        Assert.That(iPhis.Count, Is.EqualTo(1));

        var phi = iPhis[0];
        Assert.That(header.Instructions, Does.Contain(phi));
        Assert.That(phi.Operands.Count, Is.EqualTo(1 + header.Predecessors.Count));
    }
}
