using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ISIL;

public class Instruction(int index, OpCode opcode, params object[] operands)
{
    public int Index = index;

    public OpCode OpCode = opcode;

    public List<object> Operands = operands.ToList();

    public bool IsFallThrough =>
        OpCode switch
        {
            OpCode.Return or OpCode.Jump or OpCode.ConditionalJump or OpCode.IndirectJump or OpCode.Throw => false,
            _ => true
        };

    public bool IsCall => OpCode is OpCode.Call or OpCode.CallVoid;

    public bool IsAssignment => Destination != null;

    public List<object> Sources => GetSources();

    public List<object> SourcesAndConstants => GetSources(false);

    public object? Destination
    {
        get => GetOrSetDestination();
        set => GetOrSetDestination(value);
    }

    private object? GetOrSetDestination(object? newDestination = null)
    {
        switch (OpCode)
        {
            case OpCode.Move:
            case OpCode.Phi:
            case OpCode.Add:
            case OpCode.Subtract:
            case OpCode.Multiply:
            case OpCode.Divide:
            case OpCode.ShiftLeft:
            case OpCode.ShiftRight:
            case OpCode.And:
            case OpCode.Or:
            case OpCode.Xor:
            case OpCode.Not:
            case OpCode.Negate:
            case OpCode.CheckEqual:
            case OpCode.CheckGreater:
            case OpCode.CheckLess:
            case OpCode.CheckNotEqual:
            case OpCode.CheckGreaterOrEqual:
            case OpCode.CheckLessOrEqual:
            case OpCode.Newobj:
                if (newDestination != null)
                    Operands[0] = newDestination;
                return IsConstantValue(Operands[0]) ? null : Operands[0];

            // A call's operand 0 is the target; its return value is operand 1 (per OpCode.Call).
            // CallVoid has no return value and so has no destination, and a Call may also be emitted
            // without a return-value operand, in which case it likewise has no destination.
            case OpCode.Call:
            case OpCode.IndirectCall:
                if (Operands.Count < 2)
                    return null;
                if (newDestination != null)
                    Operands[1] = newDestination;
                return IsConstantValue(Operands[1]) ? null : Operands[1];

            default:
                return null;
        }
    }

    private List<object> GetSources(bool constantsOnly = true)
    {
        var sources = OpCode switch
        {
            OpCode.Move or OpCode.ConditionalJump
                or OpCode.ShiftStack or OpCode.Not or OpCode.Negate
                or OpCode.Newobj
                => [Operands[1]],

            OpCode.Add or OpCode.Subtract or OpCode.Multiply
                or OpCode.Divide or OpCode.ShiftLeft or OpCode.ShiftRight
                or OpCode.And or OpCode.Or or OpCode.Xor
                => [Operands[2], Operands[1]],

            OpCode.Call => Operands.Skip(2).ToList(),

            // Unlike a direct call, operand 0 is the address being called and so is itself a source.
            OpCode.IndirectCall => Operands.Count > 2
                ? Operands.Skip(2).Prepend(Operands[0]).ToList()
                : Operands.Take(1).ToList(),

            OpCode.CallVoid or OpCode.Phi => Operands.Skip(1).ToList(),
            OpCode.CheckEqual or OpCode.CheckGreater or OpCode.CheckLess
                or OpCode.CheckNotEqual or OpCode.CheckGreaterOrEqual or OpCode.CheckLessOrEqual
                => [Operands[1], Operands[2]],

            _ => []
        };

        if (OpCode == OpCode.Return && Operands.Count == 1)
            sources.Add(Operands[0]);

        if (constantsOnly)
            sources = sources.Where(o => !IsConstantValue(o)).ToList();

        return sources;
    }

    public override string ToString()
    {
        if (OpCode == OpCode.Jump && Operands[0] is ulong jumpTarget)
            return $"{Index} {OpCode} {jumpTarget:X4}";
        if (OpCode == OpCode.ConditionalJump && Operands[0] is ulong jumpTarget2)
            return $"{Index} {OpCode} {jumpTarget2:X4}, {FormatOperand(Operands[1])}";

        if ((OpCode is OpCode.CallVoid or OpCode.Call) && Operands[0] is ulong callTarget)
        {
            var remainingOperands = string.Join(", ", Operands.Skip(1).Select(FormatOperand));
            return string.IsNullOrEmpty(remainingOperands)
                ? $"{Index} {OpCode} {callTarget:X4}"
                : $"{Index} {OpCode} {callTarget:X4}, {remainingOperands}";
        }

        var formattedOperands = string.Join(", ", Operands.Select(FormatOperand));
        return string.IsNullOrEmpty(formattedOperands)
            ? $"{Index} {OpCode}"
            : $"{Index} {OpCode} {formattedOperands}";
    }

    private static string FormatOperand(object operand)
    {
        return operand switch
        {
            string text => $"\"{text}\"",
            // 'f'/'d' suffixes keep a reinterpreted float literal from reading as a plain integer.
            float f => $"{f.ToString(CultureInfo.InvariantCulture)}f",
            double d => $"{d.ToString(CultureInfo.InvariantCulture)}d",
            MethodAnalysisContext method => $"{method.DeclaringType!.Name}.{method.Name}",
            RuntimeMethodInfoAnalysisContext methodInfo => $"methodof({methodInfo.RepresentedMethod.FullName})",
            TypeAnalysisContext type => $"typeof({type.FullName})",
            Instruction instruction => $"@{instruction.Index}",
            Block block => $"@b{block.ID}",
            _ => operand.ToString()!
        };
    }

    public static bool IsConstantValue(object operand) =>
        operand switch
        {
            Register or StackOffset or LocalVariable => false,
            MemoryOperand memory => memory.IsConstant,
            _ => true
        };

    // Deliberately not Equals/GetHashCode. Instructions are identity objects: the graph, stack analyzer and
    // IL generator all key sets and dictionaries on the specific instruction instance, and generated
    // instructions (phi copies, for one) are routinely structurally identical to each other.
    public bool IsStructurallyEqualTo(Instruction? other)
    {
        if (ReferenceEquals(this, other))
            return true;

        if (other is null)
            return false;

        if (OpCode != other.OpCode)
            return false;

        if (Index != other.Index)
            return false;

        if (Operands.Count != other.Operands.Count)
            return false;

        for (var i = 0; i < Operands.Count; i++)
        {
            var thisOperand = Operands[i];
            var otherOperand = other.Operands[i];

            // Branch targets are compared by index, so a back edge doesn't send us round in circles.
            if (thisOperand is Instruction thisTarget)
            {
                if (otherOperand is not Instruction otherTarget || thisTarget.Index != otherTarget.Index)
                    return false;

                continue;
            }

            if (!thisOperand.Equals(otherOperand))
                return false;
        }

        return true;
    }
}
