using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Tests;

// sugar so fixtures can write raw ints/strings where operands are expected
public static class TestOperands
{
    public static Immediate Imm(long value) => new(value);

    public static Immediate Imm(ulong value) => new(unchecked((long)value));

    public static StringLiteral Str(string value) => new(value);

    public static IOperand Op(object value) => value switch
    {
        IOperand operand => operand,
        int i => new Immediate(i),
        long l => new Immediate(l),
        uint u32 => new Immediate(u32),
        ulong u => new Immediate(unchecked((long)u)),
        string s => new StringLiteral(s),
        float f => new FloatLiteral(f),
        double d => new DoubleLiteral(d),
        _ => throw new ArgumentException($"No operand conversion for {value.GetType()}")
    };

    public static List<IOperand> Ops(params object[] values) => [.. values.Select(Op)];
}
