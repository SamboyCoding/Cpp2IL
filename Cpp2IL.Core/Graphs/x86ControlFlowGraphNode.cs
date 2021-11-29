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