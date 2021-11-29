using System;

namespace Cpp2IL.Core;

public class Condition<T>
{
    protected T Comparison;
    protected T Jump;

    public Condition(T comparison, T conditionalJump)
    {
        Comparison = comparison;
        Jump = conditionalJump;
        ConditionString = GetCondition();
    }

    public string ConditionString { get; }

    public virtual string GetCondition() => throw new NotImplementedException();


    public virtual string GetConditionOperator() => throw new NotImplementedException();
}