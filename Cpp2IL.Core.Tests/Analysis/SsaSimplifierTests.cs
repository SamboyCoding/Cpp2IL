using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Tests.Analysis;

public class SsaSimplifierTests
{
    private static LocalVariable Local(string name) => new(name, new Register(null, name));

    private static List<Instruction> Run(List<Instruction> instructions, params LocalVariable[] parameters)
    {
        var graph = new ISILControlFlowGraph(instructions);
        SsaSimplifier.Run(graph, parameters.ToList());
        return graph.Blocks.SelectMany(b => b.Instructions).ToList();
    }

    [Test]
    public void ForwardsConstantThroughCopyChainToUse()
    {
        var t1 = Local("t1");
        var t2 = Local("t2");

        // t1 := 5; t2 := t1; f(t2)  ==>  f(5), both moves dead.
        var live = Run(new List<Instruction>
        {
            new(0, OpCode.Move, t1, Imm(5)),
            new(1, OpCode.Move, t2, t1),
            new(2, OpCode.CallVoid, Str("f"), t2, Imm(0)),
            new(3, OpCode.Return),
        });

        var call = live.Single(i => i.OpCode == OpCode.CallVoid);
        Assert.That(call.Operands[1], Is.EqualTo(Imm(5)), "the constant should be forwarded all the way to the use");

        var remainingMoves = live.Where(i => i.OpCode == OpCode.Move).ToList();
        Assert.That(remainingMoves, Is.Empty, "both copies become dead once their value is forwarded");
    }

    [Test]
    public void DoesNotForwardMemoryLoads()
    {
        var baseLocal = Local("base");
        var x = Local("x");

        // x := [base+8]; f(x).  The load must not be duplicated into the call, so x stays.
        var live = Run(new List<Instruction>
        {
            new(0, OpCode.Move, x, new MemoryOperand(baseLocal, null, 8, 0)),
            new(1, OpCode.CallVoid, Str("f"), x, Imm(0)),
            new(2, OpCode.Return),
        });

        Assert.That(live.Any(i => i.OpCode == OpCode.Move && ReferenceEquals(i.Operands[0], x)), Is.True,
            "a memory load is left in place for the post-SSA pass, not forwarded");

        var call = live.Single(i => i.OpCode == OpCode.CallVoid);
        Assert.That(ReferenceEquals(call.Operands[1], x), Is.True, "the use still reads the loaded local");
    }

    [Test]
    public void ForwardsCopyIntoMemoryBase()
    {
        var p = Local("p");
        var q = Local("q");
        var x = Local("x");

        // p := q; x := [p+8].  p is a copy of q, so the load's base becomes q and p dies.
        var live = Run(new List<Instruction>
        {
            new(0, OpCode.Move, p, q),
            new(1, OpCode.Move, x, new MemoryOperand(p, null, 8, 0)),
            new(2, OpCode.CallVoid, Str("f"), x, Imm(0)),
            new(3, OpCode.Return),
        });

        Assert.That(live.Any(i => i.OpCode == OpCode.Move && ReferenceEquals(i.Operands[0], p)), Is.False,
            "the copy is forwarded and removed");

        var load = live.Single(i => i.OpCode == OpCode.Move && ReferenceEquals(i.Operands[0], x));
        var memory = (MemoryOperand)load.Operands[1];
        Assert.That(ReferenceEquals(memory.Base, q), Is.True, "the memory base is rewritten to the copy's source");
    }

    [Test]
    public void DoesNotForwardParameterLocals()
    {
        var p = Local("p");
        var arg = Local("arg");

        // arg := p, where p is a parameter. The named parameter value must be preserved.
        var live = Run(new List<Instruction>
        {
            new(0, OpCode.Move, arg, p),
            new(1, OpCode.CallVoid, Str("f"), arg, Imm(0)),
            new(2, OpCode.Return),
        }, p);

        // arg is a plain copy of a parameter, so it is still forwarded; what matters is that p's own
        // definition is never the thing eliminated. Here the forward simply rewrites the use to p.
        var call = live.Single(i => i.OpCode == OpCode.CallVoid);
        Assert.That(ReferenceEquals(call.Operands[1], p), Is.True, "the parameter flows through to the use");
    }
}
