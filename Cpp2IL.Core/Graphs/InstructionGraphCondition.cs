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

    public string ConditionString { get; set; }

    public virtual string GetCondition(bool invert = false) => throw new NotImplementedException();

    public virtual void FlipCondition() => throw new NotImplementedException();


    public virtual string GetConditionOperator(bool invert = false) => throw new NotImplementedException();
}