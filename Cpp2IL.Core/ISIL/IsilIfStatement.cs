using System.Collections.Generic;

namespace Cpp2IL.Core.ISIL;

public class IsilIfStatement : IsilStatement
{
    public IsilCondition Condition;
    public List<IsilStatement> IfBlock = new();
    public List<IsilStatement> ElseBlock = new();

    public IsilIfStatement(IsilCondition condition)
    {
        Condition = condition;
    }

    public IsilBuilder GetIfBuilder() => new(IfBlock);
    public IsilBuilder GetElseBuilder() => new(ElseBlock);
}