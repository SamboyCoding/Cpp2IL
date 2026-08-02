using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Tests.Analysis;

public class SimplifierTests
{
    private static MethodAnalysisContext CreateMethod(ISILControlFlowGraph graph, params LocalVariable[] locals)
    {
        var method = (MethodAnalysisContext)RuntimeHelpers.GetUninitializedObject(typeof(MethodAnalysisContext));
        method.ControlFlowGraph = graph;
        method.Locals = locals.ToList();
        method.ParameterLocals = [];
        return method;
    }

    [Test]
    public void DoesNotInlineAcrossJoinWhenLocalHasMultipleDefinitions()
    {
        var x  = new LocalVariable("x", new Register(null, "x"));
        var cond = new LocalVariable("cond", new Register(null, "cond"));
        var selected = new LocalVariable("selected", new Register(null, "selected"));
        var oddText = new LocalVariable("oddText", new Register(null, "oddText"));
        var evenText = new LocalVariable("evenText", new Register(null, "evenText"));

        var instructions = new List<Instruction>
        {
            new(0, OpCode.CheckNotEqual, cond, x, Imm(1)),
            new(1, OpCode.ConditionalJump, Imm(5), cond),
            new(2, OpCode.Move, oddText, Str("Odd second")),
            new(3, OpCode.Move, selected, oddText),
            new(4, OpCode.Jump, Imm(8)),
            new(5, OpCode.Move, evenText, Str("Even second")),
            new(6, OpCode.Move, selected, evenText),
            new(7, OpCode.Jump, Imm(8)),
            new(8, OpCode.CallVoid, Str("Console.WriteLine"), selected, Imm(0)),
            new(9, OpCode.Return),
        };

        foreach (var instruction in instructions)
        {
            if (instruction.OpCode is not (OpCode.Jump or OpCode.ConditionalJump))
                continue;

            instruction.SetOperand(0, instructions[(int)((Immediate)instruction.Operands[0]).Value]);
        }

        var graph = new ISILControlFlowGraph(instructions);
        var method = CreateMethod(graph, cond, selected, oddText, evenText);

        Simplifier.Simplify(method);

        var live = graph.Blocks.SelectMany(b => b.Instructions).ToList();
        var selectedDefinitions = live.Where(i => i.OpCode == OpCode.Move && ReferenceEquals(i.Destination, selected)).ToList();
        Assert.That(selectedDefinitions.Count, Is.EqualTo(2), "both branch assignments to selected must remain");

        var writeLineCall = live.Single(i => i.OpCode == OpCode.CallVoid && i.Operands[0] is StringLiteral { Value: "Console.WriteLine" });
        Assert.That(ReferenceEquals(writeLineCall.Operands[1], selected), Is.True,
            "join-point call must keep the selected local, not a branch-specific constant");
    }

    [Test]
    public void DoesNotCorruptUnrelatedConstantMemoryAddends()
    {
        var aLocal = new LocalVariable("a", new Register(null, "a"));
        var bLocal = new LocalVariable("b", new Register(null, "b"));

        // a := [0xAAAA]; f(a); b := [0xBBBB]; g(b).  Inlining a's constant load must not touch the
        // unrelated constant address [0xBBBB] - they are distinct absolute addresses.
        var instructions = new List<Instruction>
        {
            new(0, OpCode.Move, aLocal, new MemoryOperand(null, null, 0xAAAA, 0)),
            new(1, OpCode.CallVoid, Str("f"), aLocal, Imm(0)),
            new(2, OpCode.Move, bLocal, new MemoryOperand(null, null, 0xBBBB, 0)),
            new(3, OpCode.CallVoid, Str("g"), bLocal, Imm(0)),
            new(4, OpCode.Return),
        };

        var graph = new ISILControlFlowGraph(instructions);
        var method = CreateMethod(graph, aLocal, bLocal);

        Simplifier.Simplify(method);

        var live = graph.Blocks.SelectMany(b => b.Instructions).ToList();

        var gCall = live.Single(i => i.OpCode == OpCode.CallVoid && i.Operands[0] is StringLiteral { Value: "g" });
        Assert.That(gCall.Operands[1], Is.InstanceOf<MemoryOperand>());
        Assert.That(((MemoryOperand)gCall.Operands[1]).Addend, Is.EqualTo(0xBBBBL),
            "an unrelated constant address must not be rewritten by another inline");

        // The intended inline still happens.
        var fCall = live.Single(i => i.OpCode == OpCode.CallVoid && i.Operands[0] is StringLiteral { Value: "f" });
        Assert.That(fCall.Operands[1], Is.InstanceOf<MemoryOperand>());
        Assert.That(((MemoryOperand)fCall.Operands[1]).Addend, Is.EqualTo(0xAAAAL));
    }

    [Test]
    public void PropagatesCopyThroughMemoryBase()
    {
        var x = new LocalVariable("x", new Register(null, "x"));
        var y = new LocalVariable("y", new Register(null, "y"));
        var z = new LocalVariable("z", new Register(null, "z"));

        // x := y; z := [x]; f(z).  Copy propagation must rewrite the load's base x -> y (a base must
        // stay a local), so the surviving load reads [y].
        var instructions = new List<Instruction>
        {
            new(0, OpCode.Move, x, y),
            new(1, OpCode.Move, z, new MemoryOperand(x, null, 0, 0)),
            new(2, OpCode.CallVoid, Str("f"), z, Imm(0)),
            new(3, OpCode.Return),
        };

        var graph = new ISILControlFlowGraph(instructions);
        var method = CreateMethod(graph, x, y, z);

        Simplifier.Simplify(method);

        var live = graph.Blocks.SelectMany(b => b.Instructions).ToList();
        var load = live.Single(i => i.Operands.Any(o => o is MemoryOperand));
        var memory = (MemoryOperand)load.Operands.First(o => o is MemoryOperand);
        Assert.That(ReferenceEquals(memory.Base, y), Is.True, "the copy's source must be propagated into the memory base");
        Assert.That(live.Any(i => i.OpCode == OpCode.Move && ReferenceEquals(i.Operands[0], x)), Is.False,
            "the now-dead copy is removed");
    }
}
