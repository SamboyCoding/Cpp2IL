using System;
using System.Linq;
using Iced.Intel;

namespace Cpp2IL.Core;

public class X86ControlFlowGraphNode : InstructionGraphNode<Instruction>
{
    public override string GetFormattedInstructionAddress(Instruction instruction)
    {
        return "0x" + instruction.IP.ToString("X8").ToUpperInvariant();
    }

    public override bool ThisNodeHasComparison()
    {
        return Instructions.Any(instruction => instruction.Mnemonic == Mnemonic.Cmp || instruction.Mnemonic == Mnemonic.Test);
    }

    public override void CreateCondition()
    {
        var lastInstruction = Instructions.Last();
        
        var condition = Instructions.Last(instruction => instruction.Mnemonic == Mnemonic.Test || instruction.Mnemonic == Mnemonic.Cmp);

        Condition = new X86ControlFlowGraphCondition(condition, lastInstruction);
            
        TrueTarget = Neighbors.Single(node => lastInstruction.NearBranch64 == node.Instructions[0].IP);
        FalseTarget = Neighbors.Single(node => lastInstruction.NearBranch64 != node.Instructions[0].IP);
    }
}

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
            if (Comparison.Op0Register == Comparison.Op1Register)
            {
                _formatter.FormatOperand(Comparison, _output, 0);
                return $"{_output.ToStringAndReset()} {GetConditionOperator()} 0";
            }
            _formatter.FormatOperand(Comparison, _output, 0);
            var argumentOne = _output.ToStringAndReset();
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
                return "<";
            case Mnemonic.Jle:
                return "<=";
            case Mnemonic.Ja:
                return ">";
            case Mnemonic.Jae:
                return ">=";
            case Mnemonic.Jb:
                return "<";
            case Mnemonic.Jbe:
                return "<=";
            default:
                throw new Exception($"{Jump.Mnemonic} isn't supported currently");
        }
    }
}