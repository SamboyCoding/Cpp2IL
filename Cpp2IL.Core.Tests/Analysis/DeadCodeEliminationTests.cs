using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Tests.Analysis;

public class DeadCodeEliminationTests
{
    private static List<Instruction> Live(ISILControlFlowGraph graph)
        => graph.Blocks.SelectMany(b => b.Instructions).Where(i => i.OpCode != OpCode.Nop).ToList();

    [Test]
    public void RemovesDeadDefinitionButKeepsLiveOnes()
    {
        var x = new LocalVariable("x", new Register(null, "x"));
        var dead = new LocalVariable("dead", new Register(null, "dead"));

        var graph = new ISILControlFlowGraph(new List<Instruction>
        {
            new(0, OpCode.Move, x, Imm(5)),
            new(1, OpCode.Subtract, dead, x, Imm(1)), // dead's result is never read
            new(2, OpCode.Return, x),
        });

        DeadCodeEliminator.Run(graph);

        var live = Live(graph);
        Assert.That(live.Any(i => i.OpCode == OpCode.Subtract), Is.False, "dead computation should be removed");
        Assert.That(live.Any(i => i.OpCode == OpCode.Move), Is.True, "live definition should remain");
        Assert.That(live.Any(i => i.OpCode == OpCode.Return), Is.True, "terminator should remain");
    }

    [Test]
    public void RemovesDeadChainToFixpoint()
    {
        // x = 5; temp = x - 1; flag = temp < 0; return x
        // 'flag' is unused -> dead; that makes 'temp' unused -> dead too (cascade).
        var x = new LocalVariable("x", new Register(null, "x"));
        var temp = new LocalVariable("temp", new Register(null, "temp"));
        var flag = new LocalVariable("flag", new Register(null, "flag"));

        var graph = new ISILControlFlowGraph(new List<Instruction>
        {
            new(0, OpCode.Move, x, Imm(5)),
            new(1, OpCode.Subtract, temp, x, Imm(1)),
            new(2, OpCode.CheckLess, flag, temp, Imm(0)),
            new(3, OpCode.Return, x),
        });

        DeadCodeEliminator.Run(graph);

        var live = Live(graph);
        Assert.That(live.Any(i => i.OpCode == OpCode.CheckLess), Is.False, "dead flag should be removed");
        Assert.That(live.Any(i => i.OpCode == OpCode.Subtract), Is.False, "now-dead temp should be removed (cascade)");
        Assert.That(live.Count, Is.EqualTo(2), "only the live Move and Return should remain");
    }

    [Test]
    public void KeepsCallsEvenWhenResultUnused()
    {
        // A call with no observed result must be kept (side effects).
        var graph = new ISILControlFlowGraph(new List<Instruction>
        {
            new(0, OpCode.CallVoid, Imm(0xDEADBEEFUL)),
            new(1, OpCode.Return),
        });

        DeadCodeEliminator.Run(graph);

        Assert.That(Live(graph).Any(i => i.OpCode == OpCode.CallVoid), Is.True, "calls must never be removed");
    }
}
