using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Tests.Analysis;

public class FlagConditionRecoveryTests
{
    private static readonly LocalVariable A = new("a", new Register(null, "a"));
    private static readonly LocalVariable B = new("b", new Register(null, "b"));

    private static LocalVariable Flag(string name) => new(name, new Register(null, name));

    /// <summary>
    /// Builds a graph from a flag-cluster + conditional jump, resolving the jump target like the
    /// lifter does, and returns the instruction defining <paramref name="condition"/> after recovery.
    /// </summary>
    private static Instruction RecoverAndGetConditionDef(List<Instruction> flagAndBranch, LocalVariable condition)
    {
        // Append two trivial return blocks so the conditional jump has a real target/fallthrough.
        var index = flagAndBranch.Count;
        var instructions = new List<Instruction>(flagAndBranch)
        {
            new(index, OpCode.Return),
            new(index + 1, OpCode.Return),
        };

        // The conditional jump's target (operand 0) is the last instruction's index.
        var conditionalJump = instructions.First(i => i.OpCode == OpCode.ConditionalJump);
        conditionalJump.SetOperand(0, instructions[index + 1]);

        var graph = new ISILControlFlowGraph(instructions);
        FlagConditionRecovery.Run(graph);

        return graph.Blocks.SelectMany(b => b.Instructions)
            .First(i => i.Destination is LocalVariable d && ReferenceEquals(d, condition));
    }

    [Test]
    public void RecoversEquality()
    {
        // cmp a, b ; je   ->  ZF = (a - b) == 0 ; if ZF
        var t1 = Flag("TEMP1");
        var zf = Flag("ZF");

        var def = RecoverAndGetConditionDef(new List<Instruction>
        {
            new(0, OpCode.Subtract, t1, A, B),
            new(1, OpCode.CheckEqual, zf, t1, 0),
            new(2, OpCode.ConditionalJump, 0, zf),
        }, zf);

        Assert.That(def.OpCode, Is.EqualTo(OpCode.CheckEqual));
        Assert.That(def.Operands[1], Is.EqualTo(A));
        Assert.That(def.Operands[2], Is.EqualTo(B));
    }

    [Test]
    public void RecoversInequality()
    {
        // cmp a, b ; jne  ->  ZF = (a - b) == 0 ; cond = !ZF ; if cond
        var t1 = Flag("TEMP1");
        var zf = Flag("ZF");
        var cond = Flag("TEMP");

        var def = RecoverAndGetConditionDef(new List<Instruction>
        {
            new(0, OpCode.Subtract, t1, A, B),
            new(1, OpCode.CheckEqual, zf, t1, 0),
            new(2, OpCode.Not, cond, zf),
            new(3, OpCode.ConditionalJump, 0, cond),
        }, cond);

        Assert.That(def.OpCode, Is.EqualTo(OpCode.CheckNotEqual));
        Assert.That(def.Operands[1], Is.EqualTo(A));
        Assert.That(def.Operands[2], Is.EqualTo(B));
    }

    [Test]
    public void RecoversSignedLessThan()
    {
        // cmp a, b ; jl  ->  full flag cluster ; cond = !(SF == OF) ; if cond
        var t1 = Flag("TEMP1");
        var t2 = Flag("TEMP2");
        var t3 = Flag("TEMP3");
        var t4 = Flag("TEMP4");
        var of = Flag("OF");
        var sf = Flag("SF");
        var sfEqOf = Flag("TEMP_a");
        var cond = Flag("TEMP_b");

        var def = RecoverAndGetConditionDef(new List<Instruction>
        {
            new(0, OpCode.Subtract, t1, A, B),
            new(1, OpCode.Xor, t2, A, B),
            new(2, OpCode.Xor, t3, A, t1),
            new(3, OpCode.And, t4, t2, t3),
            new(4, OpCode.CheckLess, of, t4, 0),
            new(5, OpCode.CheckLess, sf, t1, 0),
            new(6, OpCode.CheckEqual, sfEqOf, sf, of),
            new(7, OpCode.Not, cond, sfEqOf),
            new(8, OpCode.ConditionalJump, 0, cond),
        }, cond);

        Assert.That(def.OpCode, Is.EqualTo(OpCode.CheckLess));
        Assert.That(def.Operands[1], Is.EqualTo(A));
        Assert.That(def.Operands[2], Is.EqualTo(B));
    }

    [Test]
    public void RecoversSignedGreaterOrEqual()
    {
        // cmp a, b ; jge  ->  cond = (SF == OF) ; if cond
        var t1 = Flag("TEMP1");
        var t2 = Flag("TEMP2");
        var t3 = Flag("TEMP3");
        var t4 = Flag("TEMP4");
        var of = Flag("OF");
        var sf = Flag("SF");
        var cond = Flag("TEMP");

        var def = RecoverAndGetConditionDef(new List<Instruction>
        {
            new(0, OpCode.Subtract, t1, A, B),
            new(1, OpCode.Xor, t2, A, B),
            new(2, OpCode.Xor, t3, A, t1),
            new(3, OpCode.And, t4, t2, t3),
            new(4, OpCode.CheckLess, of, t4, 0),
            new(5, OpCode.CheckLess, sf, t1, 0),
            new(6, OpCode.CheckEqual, cond, sf, of),
            new(7, OpCode.ConditionalJump, 0, cond),
        }, cond);

        Assert.That(def.OpCode, Is.EqualTo(OpCode.CheckGreaterOrEqual));
        Assert.That(def.Operands[1], Is.EqualTo(A));
        Assert.That(def.Operands[2], Is.EqualTo(B));
    }

    [Test]
    public void LeavesUnrecognisedConditionsAlone()
    {
        // A conditional jump on a plain boolean local with no flag pattern must be untouched.
        var cond = Flag("someBool");
        var def = RecoverAndGetConditionDef(new List<Instruction>
        {
            new(0, OpCode.Move, cond, 1),
            new(1, OpCode.ConditionalJump, 0, cond),
        }, cond);

        Assert.That(def.OpCode, Is.EqualTo(OpCode.Move));
    }
}
