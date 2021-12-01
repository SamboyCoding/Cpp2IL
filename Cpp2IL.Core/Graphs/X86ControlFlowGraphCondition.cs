using System;
using Iced.Intel;

namespace Cpp2IL.Core.Graphs;

public class X86ControlFlowGraphCondition : Condition<Instruction>
{
    private static MasmFormatter _formatter = new();
    private static StringOutput _output = new();
    public X86ControlFlowGraphCondition(Instruction comparison, Instruction conditionalJump) : base(comparison, conditionalJump)
    {
    }

    public override string GetCondition()
    {
        if (Comparison.Mnemonic == Mnemonic.Test)
        {
            if (Comparison.Op0Kind == OpKind.Register && Comparison.Op1Kind == OpKind.Register && Comparison.Op0Register == Comparison.Op1Register)
            {
                _formatter.FormatOperand(Comparison, _output, 0);
                return $"{_output.ToStringAndReset()} {GetConditionOperator()} 0";
            }
            _formatter.FormatOperand(Comparison, _output, 0);
            var argumentOne = _output.ToStringAndReset();
            _formatter.FormatOperand(Comparison, _output, 1);
            var argumentTwo = _output.ToStringAndReset();
            return $"({argumentOne} & {argumentTwo}) {GetConditionOperator()} 0";
        }
        if(Comparison.Mnemonic == Mnemonic.Cmp)
        {
            _formatter.FormatOperand(Comparison, _output, 0);
            var argumentOne = _output.ToStringAndReset();
            _formatter.FormatOperand(Comparison, _output, 1);
            var argumentTwo = _output.ToStringAndReset();
            return $"{argumentOne} {GetConditionOperator()} {argumentTwo}";
        }
        throw new Exception($"Don't know what to do with {Comparison.Mnemonic}");
    }

    public override string GetConditionOperator()
    {
        switch (Jump.Mnemonic)
        {
            case Mnemonic.Je:
                return "==";
            case Mnemonic.Jne:
                return "!=";
            case Mnemonic.Jg:
                return ">";
            case Mnemonic.Jge:
                return ">=";
            case Mnemonic.Jl:
            case Mnemonic.Js:
                return "<";
            case Mnemonic.Jle:
                return "<=";
            case Mnemonic.Ja:
                return ">";
            case Mnemonic.Jae:
            case Mnemonic.Jns:
                return ">=";
            case Mnemonic.Jb:
                return "<";
            case Mnemonic.Jbe:
                return "<=";
            case Mnemonic.Jp:
                return "has parity idk todo"; //"low-order eight bits of result contain an even number of 1 bits"
            default:
                throw new Exception($"{Jump.Mnemonic} isn't supported currently");
        }
    }
}