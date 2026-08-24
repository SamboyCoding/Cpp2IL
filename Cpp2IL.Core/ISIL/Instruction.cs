using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ISIL;

public class Instruction : IOperand
{
    public int Index;

    public OpCode OpCode
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            ResetSources();
        }
    }

    private List<IOperand> _operands;

    public OperandList Operands => new(_operands);

    // Exists to clear the return register after a CallVoid, basically.
    public Register? ImplicitDefinition;

    public bool IsFallThrough =>
        OpCode switch
        {
            OpCode.Return or OpCode.Jump or OpCode.ConditionalJump or OpCode.IndirectJump or OpCode.Throw => false,
            _ => true
        };

    public bool IsCall => OpCode is OpCode.Call or OpCode.CallVoid;

    public bool IsAssignment => Destination != null;

    public OperandList Sources => new(_sources.Value);
    private Lazy<List<IOperand>> _sources;

    public OperandList SourcesAndConstants => new(_sourcesAndConstants.Value);
    private Lazy<List<IOperand>> _sourcesAndConstants;

    public Instruction(int index, OpCode opcode, params List<IOperand> operands)
    {
        Index = index;
        OpCode = opcode;
        _operands = operands;
        ResetSources();
    }

    [MemberNotNull(nameof(_operands))]
    public void SetOperands(params List<IOperand> operands)
    {
        _operands = operands;
        ResetSources();
    }

    public void SetOperand(int index, IOperand value)
    {
        _operands[index] = value;
        ResetSources();
    }

    public void AddOperands(IEnumerable<IOperand> operands)
    {
        _operands.AddRange(operands);
        ResetSources();
    }

    public void RemoveOperandAt(int index)
    {
        _operands.RemoveAt(index);
        ResetSources();
    }

    public IOperand? Destination
    {
        get => GetOrSetDestination();
        set => GetOrSetDestination(value);
    }

    private IOperand? GetOrSetDestination(IOperand? newDestination = null)
    {
        switch (OpCode)
        {
            case OpCode.Move:
            case OpCode.Phi:
            case OpCode.Add:
            case OpCode.Subtract:
            case OpCode.Multiply:
            case OpCode.Divide:
            case OpCode.Modulo:
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
            case OpCode.Box:
                if (newDestination != null)
                    SetOperand(0, newDestination);
                return IsConstantValue(_operands[0]) ? null : _operands[0];

            // A call's operand 0 is the target; its return value is operand 1 (per OpCode.Call).
            // CallVoid has no return value and so has no destination, and a Call may also be emitted
            // without a return-value operand, in which case it likewise has no destination.
            case OpCode.Call:
            case OpCode.IndirectCall:
                if (_operands.Count < 2)
                    return null;
                if (newDestination != null)
                    SetOperand(1, newDestination);
                return IsConstantValue(_operands[1]) ? null : _operands[1];

            default:
                return null;
        }
    }

    [MemberNotNull(nameof(_sources), nameof(_sourcesAndConstants))]
    private void ResetSources()
    {
        if (_sources is { IsValueCreated: false } && _sourcesAndConstants is { IsValueCreated: false })
        {
            return;
        }

        _sources = new Lazy<List<IOperand>>(() => GetSources());
        _sourcesAndConstants = new Lazy<List<IOperand>>(() => GetSources(false));
    }

    private List<IOperand> GetSources(bool constantsOnly = true)
    {
        var sources = OpCode switch
        {
            OpCode.Move or OpCode.ConditionalJump
                or OpCode.ShiftStack or OpCode.Not or OpCode.Negate
                or OpCode.Newobj
                => [_operands[1]],

            OpCode.Box => [_operands[2]],

            OpCode.Add or OpCode.Subtract or OpCode.Multiply
                or OpCode.Divide or OpCode.Modulo or OpCode.ShiftLeft or OpCode.ShiftRight
                or OpCode.And or OpCode.Or or OpCode.Xor
                => [_operands[2], _operands[1]],

            OpCode.Call => _operands.Skip(2).ToList(),

            // Unlike a direct call, operand 0 is the address being called and so is itself a source.
            OpCode.IndirectCall => _operands.Count > 2
                ? _operands.Skip(2).Prepend(_operands[0]).ToList()
                : _operands.Take(1).ToList(),

            OpCode.CallVoid or OpCode.Phi => _operands.Skip(1).ToList(),
            OpCode.CheckEqual or OpCode.CheckGreater or OpCode.CheckLess
                or OpCode.CheckNotEqual or OpCode.CheckGreaterOrEqual or OpCode.CheckLessOrEqual
                => [_operands[1], _operands[2]],

            _ => []
        };

        if (OpCode == OpCode.Return && _operands.Count == 1)
            sources.Add(_operands[0]);

        if (constantsOnly)
            sources = sources.Where(o => !IsConstantValue(o)).ToList();

        return sources;
    }

    public override string ToString()
    {
        if (OpCode == OpCode.Jump && _operands[0] is Immediate jumpTarget)
            return $"{Index} {OpCode} {jumpTarget.Value:X4}";
        if (OpCode == OpCode.ConditionalJump && _operands[0] is Immediate jumpTarget2)
            return $"{Index} {OpCode} {jumpTarget2.Value:X4}, {FormatOperand(_operands[1])}";

        if ((OpCode is OpCode.CallVoid or OpCode.Call) && _operands[0] is Immediate callTarget)
        {
            var remainingOperands = string.Join(", ", _operands.Skip(1).Select(FormatOperand));
            return string.IsNullOrEmpty(remainingOperands)
                ? $"{Index} {OpCode} {callTarget.Value:X4}"
                : $"{Index} {OpCode} {callTarget.Value:X4}, {remainingOperands}";
        }

        var formattedOperands = string.Join(", ", _operands.Select(FormatOperand));
        return string.IsNullOrEmpty(formattedOperands)
            ? $"{Index} {OpCode}"
            : $"{Index} {OpCode} {formattedOperands}";
    }

    private static string FormatOperand(IOperand operand)
    {
        return operand switch
        {
            MethodAnalysisContext method => $"{method.DeclaringType!.Name}.{method.Name}",
            RuntimeMethodInfoAnalysisContext methodInfo => $"methodof({methodInfo.RepresentedMethod.FullName})",
            RuntimeFieldInfoAnalysisContext fieldInfo => $"fieldof({fieldInfo.RepresentedField.DeclaringType.FullName}.{fieldInfo.RepresentedField.Name})",
            TypeAnalysisContext type => $"typeof({type.FullName})",
            Instruction instruction => $"@{instruction.Index}",
            Block block => $"@b{block.ID}",
            _ => operand.ToString()!
        };
    }

    public static bool IsConstantValue(IOperand operand) =>
        operand switch
        {
            Register or StackOffset or LocalVariable => false,
            AddressOf or ArrayAccess or ArrayLength => false,
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

        if (_operands.Count != other._operands.Count)
            return false;

        for (var i = 0; i < _operands.Count; i++)
        {
            var thisOperand = _operands[i];
            var otherOperand = other._operands[i];

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
