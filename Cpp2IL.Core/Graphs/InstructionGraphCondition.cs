using System;

namespace Cpp2IL.Core.Graphs;

public class InstructionGraphCondition<TInstruction>
{
    public TInstruction Comparison { get; }
    public TInstruction Jump { get; }
    public InstructionGraphCondition(TInstruction comparison, TInstruction conditionalJump)
    {
        Comparison = comparison;
        Jump = conditionalJump;
        ConditionString = GetCondition();
    }

    public string ConditionString { get; }

    public virtual string GetCondition() => throw new NotImplementedException();


    public virtual string GetConditionOperator() => throw new NotImplementedException();
}