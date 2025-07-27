using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
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
            OpCode.Return or OpCode.Jump or OpCode.ConditionalJump or OpCode.IndirectJump => false,
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
            case OpCode.Call:
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
                if (newDestination != null)
                    Operands[0] = newDestination;
                return IsConstantValue(Operands[0]) ? null : Operands[0];
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
                => [Operands[1]],

            OpCode.Add or OpCode.Subtract or OpCode.Multiply
                or OpCode.Divide or OpCode.ShiftLeft or OpCode.ShiftRight
                or OpCode.And or OpCode.Or or OpCode.Xor
                => [Operands[2], Operands[1]],

            OpCode.Call => Operands.Skip(2).ToList(),
            OpCode.CallVoid or OpCode.Phi => Operands.Skip(1).ToList(),
            OpCode.CheckEqual or OpCode.CheckGreater or OpCode.CheckLess
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
            return $"{Index} {OpCode} {callTarget:X4}, {string.Join(", ", Operands.Skip(1).Select(FormatOperand))}";

        return $"{Index} {OpCode} {string.Join(", ", Operands.Select(FormatOperand))}";
    }

    private static string FormatOperand(object operand)
    {
        return operand switch
        {
            string text => $"\"{text}\"",
            MethodAnalysisContext method => $"{method.DeclaringType!.Name}.{method.Name}",
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
}
