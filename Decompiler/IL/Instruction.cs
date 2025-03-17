using AsmResolver.DotNet;
using Decompiler.ControlFlow;

namespace Decompiler.IL;

/// <summary>
/// A single IL instruction.
/// </summary>
public class Instruction(int index, OpCode opcode, params object[] operands)
{
    /// <summary>
    /// Index of the instruction.
    /// </summary>
    public int Index = index;

    /// <summary>
    /// Opcode of the instruction.
    /// </summary>
    public OpCode OpCode = opcode;

    /// <summary>
    /// Operands for the instruction.
    /// Valid types: int, long, ulong, string, local, instruction (branch target), block, register, stack offset, method definition.
    /// </summary>
    public List<object> Operands = operands.ToList();

    /// <summary>
    /// True if the instruction doesn't affect control flow.
    /// </summary>
    public bool IsFallThrough =>
        OpCode switch
        {
            OpCode.Return or OpCode.Jump or OpCode.ConditionalJump => false,
            _ => true
        };

    /// <summary>
    /// Is the instruction a call?
    /// </summary>
    public bool IsCall => OpCode is OpCode.Call or OpCode.CallVoid or OpCode.TailCall or OpCode.TailCallVoid;

    /// <summary>
    /// Is the instruction a tail call?
    /// </summary>
    public bool IsTailCall => OpCode is OpCode.TailCall or OpCode.TailCallVoid;

    /// <summary>
    /// Is the instruction return?
    /// </summary>
    public bool IsReturn => OpCode is OpCode.Return or OpCode.ReturnVoid;

    /// <summary>
    /// Operands that the instruction uses (not including constant values).
    /// </summary>
    public List<object> Sources
    {
        get
        {
            return OpCode switch
            {
                OpCode.Move or OpCode.LoadAddress or OpCode.ConditionalJump
                    or OpCode.ShiftStack or OpCode.Not or OpCode.Negate
                    => IsConstantValue(Operands[1]) ? [] : [Operands[1]],

                OpCode.Add or OpCode.Subtract or OpCode.Multiply
                    or OpCode.Divide or OpCode.ShiftLeft or OpCode.ShiftRight
                    or OpCode.And or OpCode.Or or OpCode.Xor
                    => IsConstantValue(Operands[1]) ? IsConstantValue(Operands[2]) ? [] : [Operands[2]] : [Operands[1]],

                OpCode.Phi => Operands.Skip(1).Where(o => !IsConstantValue(o)).ToList(),
                OpCode.Call or OpCode.TailCall => Operands.Skip(2).Where(o => !IsConstantValue(o)).ToList(),
                OpCode.CallVoid or OpCode.TailCallVoid => Operands.Skip(1).Where(o => !IsConstantValue(o)).ToList(),
                OpCode.Return => IsConstantValue(Operands[0]) ? [] : [Operands[0]],
                OpCode.CheckEqual or OpCode.CheckGreater or OpCode.CheckLess
                    => new List<object> { Operands[1], Operands[2] }.Where(o => !IsConstantValue(o)).ToList(),
                _ => []
            };
        }
    }

    /// <summary>
    /// If the instruction assigns a value to something, this is the destination.
    /// </summary>
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
            case OpCode.LoadAddress:
            case OpCode.Phi:
            case OpCode.Call:
            case OpCode.TailCall:
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

    public override string ToString() => $"{Index} {OpCode} {string.Join(", ", Operands.Select(FormatOperand))}";

    private static string FormatOperand(object operand)
    {
        return operand switch
        {
            string text => $"\"{text}\"",
            MethodDefinition method => $"{method.DeclaringType!.Name}.{method.Name}",
            Instruction instruction => $"@{instruction.Index}",
            Block block => $"@b{block.Id}",
            _ => operand.ToString()!
        };
    }

    /// <summary>
    /// Checks if an operand is constant.
    /// </summary>
    /// <param name="operand">The operand.</param>
    /// <returns>True if it's constant.</returns>
    public static bool IsConstantValue(object operand) =>
        operand switch
        {
            LocalVariable or Register or StackOffset => false,
            MemoryAddress memory => memory.IsConstant,
            _ => true
        };
}
