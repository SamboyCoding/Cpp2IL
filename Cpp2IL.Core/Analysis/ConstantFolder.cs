using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

// Folds operations whose operands have become constant, and applies algebraic identities.
public static class ConstantFolder
{
    public static bool Run(MethodAnalysisContext method) => Run(method.ControlFlowGraph!);

    public static bool Run(ISILControlFlowGraph cfg)
    {
        var changed = false;

        foreach (var instruction in cfg.Instructions)
            changed |= TryFold(instruction);

        return changed;
    }

    private static bool TryFold(Instruction instruction)
    {
        // Unary constant folds.
        if (instruction is { OpCode: OpCode.Not, Operands: [_, Immediate n] })
            return ToConstant(instruction, ~n.Value);
        if (instruction is { OpCode: OpCode.Negate, Operands: [_, Immediate m] })
            return ToConstant(instruction, -m.Value);

        // metadata handles are by definition never null
        if (instruction is { OpCode: OpCode.CheckEqual or OpCode.CheckNotEqual, Operands: [_, TypeAnalysisContext, var against] }
            && Constant(against) == 0)
            return ToConstant(instruction, instruction.OpCode == OpCode.CheckEqual ? 0 : 1);

        // Binary constant folds.
        if (BinaryConstants(instruction, out var a, out var b))
        {
            switch (instruction.OpCode)
            {
                case OpCode.CheckEqual: return ToConstant(instruction, a == b ? 1 : 0);
                case OpCode.CheckNotEqual: return ToConstant(instruction, a != b ? 1 : 0);
                case OpCode.And: return ToConstant(instruction, a & b);
                case OpCode.Or: return ToConstant(instruction, a | b);
                case OpCode.Xor: return ToConstant(instruction, a ^ b);
                case OpCode.ShiftLeft: return ToConstant(instruction, a << (int)(b & 0x3F));
                case OpCode.ShiftRight: return ToConstant(instruction, a >> (int)(b & 0x3F));
            }
        }

        // One-constant algebraic identities.
        switch (instruction.OpCode)
        {
            case OpCode.And:
                if (Constant(instruction.Operands[1]) == 0 || Constant(instruction.Operands[2]) == 0)
                    return ToConstant(instruction, 0);
                // x & 1 == x only when x is a 0/1 boolean
                return BooleanIdentity(instruction, 1) is { } andBool && ToMove(instruction, andBool);
            case OpCode.Or:
            case OpCode.Xor:
            case OpCode.Add:
                return Identity(instruction, 0);
            case OpCode.Multiply:
                return Identity(instruction, 1);
            case OpCode.Subtract:
            case OpCode.ShiftLeft:
            case OpCode.ShiftRight:
                // right identity only: x - 0 / x << 0 / x >> 0 == x, but 0 - x etc. are not
                return Constant(instruction.Operands[2]) == 0 && ToMove(instruction, instruction.Operands[1]);
        }

        return false;
    }

    // For a commutative op, if one operand is the identity constant, the result is the other operand.
    private static bool Identity(Instruction instruction, long identity)
    {
        if (Constant(instruction.Operands[1]) == identity)
            return ToMove(instruction, instruction.Operands[2]);
        if (Constant(instruction.Operands[2]) == identity)
            return ToMove(instruction, instruction.Operands[1]);
        return false;
    }

    // The boolean operand of `bool & const`, or null if that isn't the shape.
    private static IOperand? BooleanIdentity(Instruction instruction, long constant)
    {
        if (Constant(instruction.Operands[2]) == constant && IsBoolean(instruction.Operands[1]))
            return instruction.Operands[1];
        if (Constant(instruction.Operands[1]) == constant && IsBoolean(instruction.Operands[2]))
            return instruction.Operands[2];
        return null;
    }

    private static bool IsBoolean(IOperand operand) => operand is LocalVariable { Type.FullName: "System.Boolean" };

    private static bool BinaryConstants(Instruction instruction, out long left, out long right)
    {
        (left, right) = (0, 0);

        if (instruction.Operands is [_, Immediate a, Immediate b])
        {
            (left, right) = (a.Value, b.Value);
            return true;
        }

        return false;
    }

    private static long? Constant(IOperand operand) => operand is Immediate immediate ? immediate.Value : null;

    private static bool ToConstant(Instruction instruction, long value)
    {
        instruction.OpCode = OpCode.Move;
        instruction.SetOperands(instruction.Operands[0], new Immediate(value));
        return true;
    }

    private static bool ToMove(Instruction instruction, IOperand source)
    {
        instruction.OpCode = OpCode.Move;
        instruction.SetOperands(instruction.Operands[0], source);
        return true;
    }
}
