using Iced.Intel;

namespace Cpp2IL.Core.Graphs;

public class X86InstructionGraphCondition : InstructionGraphCondition<Instruction>
{
    private static MasmFormatter _formatter = new();
    private static StringOutput _output = new();
    public X86InstructionGraphCondition(Instruction comparison, Instruction conditionalJump) : base(comparison, conditionalJump)
    {
    }

    public override string GetCondition(bool invert = false)
    {
        if (Comparison.Mnemonic == Mnemonic.Test)
        {
            if (Comparison.Op0Kind == OpKind.Register && Comparison.Op1Kind == OpKind.Register && Comparison.Op0Register == Comparison.Op1Register)
            {
                _formatter.FormatOperand(Comparison, _output, 0);
                return $"{_output.ToStringAndReset()} {GetConditionOperator(invert)} 0";
            }
            _formatter.FormatOperand(Comparison, _output, 0);
            var argumentOne = _output.ToStringAndReset();
            _formatter.FormatOperand(Comparison, _output, 1);
            var argumentTwo = _output.ToStringAndReset();
            return $"({argumentOne} & {argumentTwo}) {GetConditionOperator(invert)} 0";
        }
        if(Comparison.Mnemonic == Mnemonic.Cmp)
        {
            _formatter.FormatOperand(Comparison, _output, 0);
            var argumentOne = _output.ToStringAndReset();
            _formatter.FormatOperand(Comparison, _output, 1);
            var argumentTwo = _output.ToStringAndReset();
            return $"{argumentOne} {GetConditionOperator(invert)} {argumentTwo}";
        }
        throw new Exception($"Don't know what to do with {Comparison.Mnemonic}");
    }

    public override void FlipCondition()
    {
        ConditionString = GetCondition(true);
    }

    public override string GetConditionOperator(bool invert = false)
    {
        switch (Jump.Mnemonic)
        {
            case Mnemonic.Je:
                return invert ? "==": "!=";
            case Mnemonic.Jne:
                return invert ? "!=": "==";
            case Mnemonic.Jg:
                return invert ? ">": "<";
            case Mnemonic.Jge:
                return invert ? ">=": "<=";
            case Mnemonic.Jl:
            case Mnemonic.Js:
                return invert ? "<": ">";
            case Mnemonic.Jle:
                return invert ? "<=": ">=";
            case Mnemonic.Ja:
                return invert ? ">": "<";
            case Mnemonic.Jae:
            case Mnemonic.Jns:
                return invert ? ">=": "<=";
            case Mnemonic.Jb:
                return invert ? "<": ">";
            case Mnemonic.Jbe:
                return invert ? "<=": ">=";
            case Mnemonic.Jp:
                return "has parity idk todo"; //"low-order eight bits of result contain an even number of 1 bits"
            default:
                throw new Exception($"{Jump.Mnemonic} isn't supported currently");
        }
    }
}