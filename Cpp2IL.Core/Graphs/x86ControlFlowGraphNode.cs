using System.Linq;
using Iced.Intel;

namespace Cpp2IL.Core.Graphs;

public class X86ControlFlowGraphNode : InstructionGraphNode<Instruction>
{
    public override string GetFormattedInstructionAddress(Instruction instruction)
    {
        return "0x" + instruction.IP.ToString("X8").ToUpperInvariant();
    }

    public override bool ThisNodeHasComparison()
    {
        return Instructions.Any(instruction => instruction.Mnemonic is Mnemonic.Cmp or Mnemonic.Test);
    }

    protected override Instruction GetLastComparison() => Instructions.Last(instruction => instruction.Mnemonic is Mnemonic.Test or Mnemonic.Cmp);

    protected override void CreateCondition(Instruction comparison)
    {
        var lastInstruction = Instructions.Last();

        Condition = new X86ControlFlowGraphCondition(comparison, lastInstruction);
            
        TrueTarget = Successors.Single(node => lastInstruction.NearBranch64 == node.Instructions[0].IP);
        FalseTarget = Successors.Single(node => lastInstruction.NearBranch64 != node.Instructions[0].IP);
    }
}